using System.Text;
using Microsoft.Extensions.Logging;
using MunicipalityChatbot.Application.Abstractions;
using PDFtoImage;

namespace MunicipalityChatbot.Infrastructure.Services;

/// <summary>
/// Shared helper: renders scanned PDF pages to images and extracts text via an OCR service.
/// Used by both IngestionService (document uploads) and WebsiteCrawlerService (crawled PDFs).
/// </summary>
public static class PdfOcrHelper
{
    public static async Task<string> ExtractTextWithOcrAsync(
        byte[] pdfBytes, IOcrService ocr, ILogger logger, CancellationToken ct)
    {
        var pageCount = Conversion.GetPageCount(pdfBytes);
        logger.LogInformation("Starting OCR for scanned PDF ({PageCount} pages)", pageCount);

        var sb = new StringBuilder();

        for (var i = 0; i < pageCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            logger.LogInformation("OCR processing page {Page}/{Total}", i + 1, pageCount);

            try
            {
                using var imageStream = new MemoryStream();
                Conversion.SavePng(imageStream, pdfBytes, page: i, password: null, new RenderOptions { Dpi = 200 });
                var imageBytes = imageStream.ToArray();

                var pageText = await ocr.ExtractTextFromImageAsync(imageBytes, ct);
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    sb.AppendLine($"--- Page {i + 1} ---");
                    sb.AppendLine(pageText);
                    sb.AppendLine();
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OCR failed for page {Page}/{Total}, skipping", i + 1, pageCount);
            }
        }

        var result = sb.ToString();
        logger.LogInformation("OCR completed: extracted {Length} characters from {PageCount} pages", result.Length, pageCount);
        return result;
    }
}
