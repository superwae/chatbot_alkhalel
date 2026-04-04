using System.Text;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using UglyToad.PdfPig;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MunicipalityChatbot.Infrastructure.Services;

public sealed class IngestionService(
    IDocumentRepository docs,
    IEmbeddingService embeddings,
    IQdrantService qdrant,
    IOcrService? ocr,
    ILogger<IngestionService> logger
) : IIngestionService
{
    public async Task IngestDocumentAsync(Guid docId, string filename, string fileType, Stream fileStream, CancellationToken ct)
    {
        string text;

        try
        {
            text = fileType.ToLowerInvariant() switch
            {
                "docx" => ExtractTextFromDocx(fileStream),
                "xlsx" => ExtractTextFromXlsx(fileStream),
                "xls" => ExtractTextFromXlsx(fileStream), // Try xlsx parser, may fail for old binary xls
                "txt" => await ExtractTextFromTxtAsync(fileStream, ct),
                "pdf" => await ExtractTextFromPdfAsync(fileStream, ct),
                "doc" => throw new NotSupportedException("Old .doc format not supported. Please convert to .docx"),
                _ => await ExtractTextFromTxtAsync(fileStream, ct) // Default: try as text
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract text from {Filename} ({FileType}). Trying as plain text.", filename, fileType);
            fileStream.Position = 0;
            text = await ExtractTextFromTxtAsync(fileStream, ct);
        }

        // Sanitize text: remove null bytes that PostgreSQL doesn't allow
        text = text?.Replace("\0", string.Empty) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning("No extractable text found for {Filename} ({FileType}).", filename, fileType);
            return;
        }

        logger.LogInformation("Extracted {Length} characters from {Filename}", text.Length, filename);

        var chunks = Chunk(text, chunkSize: 900, overlap: 150)
            .Select((t, idx) => new DocumentChunk
            {
                ChunkId = Guid.NewGuid(),
                DocId = docId,
                Filename = filename,
                FileType = fileType,
                Language = null,
                PageNumber = null,
                SheetName = null,
                ChunkIndex = idx,
                Text = t
            })
            .ToList();

        logger.LogInformation("Created {Count} chunks for {Filename}", chunks.Count, filename);

        await docs.AddChunksAsync(chunks, ct);

        // Embed all chunks in parallel for better performance
        var embeddingTasks = chunks.Select(async chunk =>
        {
            var vec = await embeddings.EmbedAsync(chunk.Text, ct);
            return (chunk, vec);
        }).ToList();

        var embeddedChunks = await Task.WhenAll(embeddingTasks);

        // Upsert to Qdrant in parallel
        var upsertTasks = embeddedChunks.Select(item =>
            qdrant.UpsertDocChunkAsync(item.chunk, item.vec, ct)
        );

        await Task.WhenAll(upsertTasks);

        logger.LogInformation("Successfully indexed {Count} chunks to Qdrant for {Filename}", chunks.Count, filename);
    }

    private static string ExtractTextFromDocx(Stream stream)
    {
        // Copy to memory stream since OpenXml needs seekable stream
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;

        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return string.Empty;

        var sb = new StringBuilder();

        // Process all child elements in order to maintain document structure
        foreach (var element in body.ChildElements)
        {
            if (element is DocumentFormat.OpenXml.Wordprocessing.Paragraph para)
            {
                var text = para.InnerText;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                }
            }
            else if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
            {
                // Extract table content with structure preserved
                sb.AppendLine(ExtractTableText(table));
            }
        }

        return sb.ToString();
    }

    private static string ExtractTableText(DocumentFormat.OpenXml.Wordprocessing.Table table)
    {
        var sb = new StringBuilder();

        foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
        {
            var cellTexts = new List<string>();
            foreach (var cell in row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>())
            {
                // Get all text from the cell (may contain multiple paragraphs)
                var cellText = string.Join(" ", cell.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                    .Select(p => p.InnerText?.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t)));

                cellTexts.Add(cellText);
            }

            if (cellTexts.Count > 0 && cellTexts.Any(t => !string.IsNullOrWhiteSpace(t)))
            {
                sb.AppendLine(string.Join(" | ", cellTexts));
            }
        }

        sb.AppendLine(); // Separate tables with blank line
        return sb.ToString();
    }

    private static string ExtractTextFromXlsx(Stream stream)
    {
        // Copy to memory stream since OpenXml needs seekable stream
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;

        using var doc = SpreadsheetDocument.Open(ms, false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart == null) return string.Empty;

        var sb = new StringBuilder();
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
            .Elements<SharedStringItem>()
            .Select(s => s.InnerText)
            .ToArray() ?? Array.Empty<string>();

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
            if (sheetData == null) continue;

            foreach (var row in sheetData.Elements<Row>())
            {
                var rowTexts = new List<string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    var cellValue = GetCellValue(cell, sharedStrings);
                    if (!string.IsNullOrWhiteSpace(cellValue))
                        rowTexts.Add(cellValue);
                }
                if (rowTexts.Count > 0)
                    sb.AppendLine(string.Join(" | ", rowTexts));
            }
            sb.AppendLine(); // Separate sheets
        }

        return sb.ToString();
    }

    private static string GetCellValue(Cell cell, string[] sharedStrings)
    {
        if (cell.CellValue == null) return string.Empty;

        var value = cell.CellValue.InnerText;

        // If the cell uses shared strings, look up the actual value
        if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
        {
            if (int.TryParse(value, out var idx) && idx >= 0 && idx < sharedStrings.Length)
                return sharedStrings[idx];
        }

        return value;
    }

    private static async Task<string> ExtractTextFromTxtAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(ct);
    }

    private async Task<string> ExtractTextFromPdfAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var pdfBytes = ms.ToArray();

        // Always use Vision OCR when available — handles scanned pages, mixed PDFs,
        // and produces better Arabic text than basic PdfPig extraction
        if (ocr != null)
        {
            logger.LogInformation("Extracting PDF text via vision model (all pages)...");
            return await PdfOcrHelper.ExtractTextWithOcrAsync(pdfBytes, ocr, logger, ct);
        }

        // Fallback to PdfPig basic text extraction only if OCR service is unavailable
        logger.LogWarning("OCR service not available, falling back to basic PDF text extraction");
        var sb = new StringBuilder();
        using var document = PdfDocument.Open(new MemoryStream(pdfBytes));
        foreach (var page in document.GetPages())
        {
            var pageText = page.Text;
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                sb.AppendLine(pageText);
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    private static IEnumerable<string> Chunk(string text, int chunkSize, int overlap)
    {
        var t = text.Replace("\r\n", "\n");
        var i = 0;
        while (i < t.Length)
        {
            var len = Math.Min(chunkSize, t.Length - i);
            var part = t.Substring(i, len).Trim();
            if (!string.IsNullOrWhiteSpace(part))
                yield return part;
            i += (chunkSize - overlap);
            if (i < 0) break;
        }
    }
}
