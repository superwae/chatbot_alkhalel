using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Domain.Entities;
using MunicipalityChatbot.Infrastructure.Db;
using MunicipalityChatbot.Infrastructure.Services;
using UglyToad.PdfPig;

namespace MunicipalityChatbot.Api.Controllers;

[ApiController]
[Route("api/website-crawl")]
public sealed class WebsiteCrawlController(
    IWebsiteCrawlerService crawler,
    IQdrantService qdrant,
    IEmbeddingService embeddings,
    IOcrService? ocr,
    AppDbContext db,
    ILogger<WebsiteCrawlController> logger
) : ControllerBase
{
    private const string DefaultWebsiteUrl = "https://www.hebron-city.ps";
    private const string EditorRoles = $"{EmployeeRoles.EmployeeAdmin},{EmployeeRoles.EmployeeEditor}";

    public sealed record CrawlStatusResponse(
        int TotalPages,
        int TotalChunks,
        DateTimeOffset? LastCrawledAt,
        List<PageInfo> RecentPages
    );

    public sealed record PageInfo(string Url, string Title, int ChunkCount, DateTimeOffset LastCrawledAt);

    // --- DTOs for ingestion endpoints ---
    public sealed record IngestPageRequest(string Url, string Title, string Text);
    public sealed record IngestPageResponse(bool Processed, int ChunkCount, string? Reason);
    public sealed record FinalizeRequest(List<string> CrawledUrls);
    public sealed record FinalizeResponse(int RemovedPages);

    [HttpGet("status")]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin},{EmployeeRoles.EmployeeEditor},{EmployeeRoles.EmployeeViewer}")]
    public async Task<ActionResult<CrawlStatusResponse>> GetStatus(CancellationToken ct)
    {
        var pages = await db.CrawledPages
            .OrderByDescending(p => p.LastCrawledAt)
            .ToListAsync(ct);

        var totalChunks = pages.Sum(p => p.ChunkCount);
        var lastCrawled = pages.FirstOrDefault()?.LastCrawledAt;

        var recentPages = pages.Take(10).Select(p => new PageInfo(
            p.Url,
            p.Title,
            p.ChunkCount,
            p.LastCrawledAt
        )).ToList();

        return Ok(new CrawlStatusResponse(pages.Count, totalChunks, lastCrawled, recentPages));
    }

    [HttpPost("trigger")]
    [Authorize(Roles = EditorRoles)]
    public async Task<ActionResult<WebsiteCrawlResult>> TriggerCrawl([FromQuery] string? url, CancellationToken ct)
    {
        var targetUrl = string.IsNullOrWhiteSpace(url) ? DefaultWebsiteUrl : url;

        var result = await crawler.CrawlWebsiteAsync(targetUrl, ct);

        return Ok(result);
    }

    [HttpDelete("clear")]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin}")]
    public async Task<ActionResult> ClearAll(CancellationToken ct)
    {
        // Delete all website chunks from Qdrant
        await qdrant.DeleteAllWebsiteChunksAsync(ct);

        // Delete all crawled pages and their chunks from database
        await db.DocumentChunks.Where(c => c.FileType == "website").ExecuteDeleteAsync(ct);
        await db.CrawledPages.ExecuteDeleteAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// Ingest a single crawled page (text content). Called by external crawler (e.g. Crawl4AI Python script).
    /// </summary>
    [HttpPost("ingest-page")]
    [Authorize(Roles = EditorRoles)]
    public async Task<ActionResult<IngestPageResponse>> IngestPage([FromBody] IngestPageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest("URL is required.");

        var text = request.Text?.Trim() ?? "";

        if (text.Length < 50)
            return Ok(new IngestPageResponse(false, 0, $"Content too short ({text.Length} chars)"));

        var chunkCount = await IngestContentAsync(request.Url, request.Title ?? "", text, ct);
        return Ok(new IngestPageResponse(true, chunkCount, null));
    }

    /// <summary>
    /// Ingest a PDF found during crawl. Runs OCR via Vision model, then chunks and stores.
    /// </summary>
    [HttpPost("ingest-pdf")]
    [Authorize(Roles = EditorRoles)]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<IngestPageResponse>> IngestPdf(
        [FromForm] string url,
        [FromForm] IFormFile file,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return BadRequest("URL is required.");
        if (file is null || file.Length == 0)
            return BadRequest("PDF file is required.");

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var pdfBytes = ms.ToArray();

        // Extract title from PDF metadata
        var title = Path.GetFileNameWithoutExtension(file.FileName);
        try
        {
            using var pdfDoc = PdfDocument.Open(new MemoryStream(pdfBytes));
            if (!string.IsNullOrWhiteSpace(pdfDoc.Information.Title))
                title = pdfDoc.Information.Title.Trim();
        }
        catch { /* Use filename as title */ }

        // Extract text via OCR
        string text;
        if (ocr != null)
        {
            logger.LogInformation("PDF [{Url}]: Extracting text via vision model...", url);
            text = await PdfOcrHelper.ExtractTextWithOcrAsync(pdfBytes, ocr, logger, ct);
        }
        else
        {
            // Fallback to PdfPig
            logger.LogWarning("PDF [{Url}]: OCR not available, using basic extraction", url);
            var sb = new StringBuilder();
            using var pdfDoc = PdfDocument.Open(new MemoryStream(pdfBytes));
            foreach (var page in pdfDoc.GetPages())
            {
                var pageText = page.Text;
                if (!string.IsNullOrWhiteSpace(pageText))
                    sb.AppendLine(pageText);
            }
            text = sb.ToString();
        }

        text = Regex.Replace(text, @"\s+", " ").Trim();

        if (text.Length < 50)
            return Ok(new IngestPageResponse(false, 0, $"PDF content too short ({text.Length} chars)"));

        var chunkCount = await IngestContentAsync(url, title, text, ct);
        return Ok(new IngestPageResponse(true, chunkCount, null));
    }

    /// <summary>
    /// Finalize a crawl session: remove pages that no longer exist on the website.
    /// </summary>
    [HttpPost("finalize")]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin}")]
    public async Task<ActionResult<FinalizeResponse>> Finalize([FromBody] FinalizeRequest request, CancellationToken ct)
    {
        if (request.CrawledUrls is null || request.CrawledUrls.Count == 0)
            return Ok(new FinalizeResponse(0));

        var crawledSet = new HashSet<string>(request.CrawledUrls, StringComparer.OrdinalIgnoreCase);

        var allPages = await db.CrawledPages.ToListAsync(ct);
        var orphaned = allPages.Where(p => !crawledSet.Contains(p.Url)).ToList();

        foreach (var page in orphaned)
        {
            await qdrant.DeleteWebsiteChunksAsync(page.PageId, ct);
            await db.DocumentChunks.Where(c => c.DocId == page.PageId).ExecuteDeleteAsync(ct);
            db.CrawledPages.Remove(page);
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Finalize: removed {Count} orphaned pages", orphaned.Count);
        return Ok(new FinalizeResponse(orphaned.Count));
    }

    // --- Shared ingestion pipeline ---

    private async Task<int> IngestContentAsync(string url, string title, string text, CancellationToken ct)
    {
        var hash = WebsiteCrawlerService.ComputeHash(text);

        // Check if page already exists with same content
        var existing = await db.CrawledPages.FirstOrDefaultAsync(p => p.Url == url, ct);
        if (existing != null && existing.ContentHash == hash)
        {
            logger.LogInformation("SKIPPED (unchanged): [{Title}] {Url}", title, url);
            return 0;
        }

        // Delete old chunks if page exists with different content
        if (existing != null)
        {
            await qdrant.DeleteWebsiteChunksAsync(existing.PageId, ct);
            await db.DocumentChunks.Where(c => c.DocId == existing.PageId).ExecuteDeleteAsync(ct);
            existing.ContentHash = hash;
            existing.Title = title;
            existing.LastCrawledAt = DateTimeOffset.UtcNow;
        }
        else
        {
            existing = new CrawledPage
            {
                PageId = Guid.NewGuid(),
                Url = url,
                Title = title,
                ContentHash = hash,
                CreatedAt = DateTimeOffset.UtcNow,
                LastCrawledAt = DateTimeOffset.UtcNow
            };
            db.CrawledPages.Add(existing);
        }

        // Chunk text
        var chunks = WebsiteCrawlerService.Chunk(text, WebsiteCrawlerService.ChunkSize, WebsiteCrawlerService.ChunkOverlap)
            .Select((t, idx) => new WebsiteChunk(
                Guid.NewGuid(),
                existing.PageId,
                url,
                title,
                idx,
                t
            ))
            .ToList();

        existing.ChunkCount = chunks.Count;
        await db.SaveChangesAsync(ct);

        // Embed and store in Qdrant
        foreach (var chunk in chunks)
        {
            var vector = await embeddings.EmbedAsync(chunk.Text, ct);
            await qdrant.UpsertWebsiteChunkAsync(chunk, vector, ct);
        }

        // Store in PostgreSQL
        var dbChunks = chunks.Select(c => new DocumentChunk
        {
            ChunkId = c.ChunkId,
            DocId = c.PageId,
            Filename = c.Url,
            FileType = "website",
            ChunkIndex = c.ChunkIndex,
            Text = c.Text
        });
        db.DocumentChunks.AddRange(dbChunks);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("INGESTED: [{Title}] {Url} - {ChunkCount} chunks", title, url, chunks.Count);
        return chunks.Count;
    }
}
