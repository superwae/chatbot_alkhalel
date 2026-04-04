using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Domain.Entities;
using MunicipalityChatbot.Infrastructure.Db;
using UglyToad.PdfPig;

namespace MunicipalityChatbot.Infrastructure.Services;

public sealed class WebsiteCrawlerService(
    HttpClient http,
    AppDbContext db,
    IEmbeddingService embeddings,
    IQdrantService qdrant,
    IOcrService? ocr,
    ILogger<WebsiteCrawlerService> logger
) : IWebsiteCrawlerService
{
    private const int MaxPages = 500;
    public const int ChunkSize = 900;
    public const int ChunkOverlap = 150;

    // Extensions to skip (media, archives, etc.) - PDFs are processed separately
    private static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".xls", ".xlsx", ".zip", ".rar",
        ".jpg", ".jpeg", ".png", ".gif", ".svg", ".ico", ".webp",
        ".mp3", ".mp4", ".avi", ".mov", ".wmv", ".css", ".js"
    };

    public async Task<WebsiteCrawlResult> CrawlWebsiteAsync(string baseUrl, CancellationToken ct)
    {
        var errors = new List<string>();
        var processed = 0;
        var skipped = 0;
        var totalChunks = 0;

        logger.LogInformation("Starting website crawl for {BaseUrl}", baseUrl);

        try
        {
            // Try to get URLs from sitemap first
            var urls = await GetUrlsFromSitemapAsync(baseUrl, ct);

            if (urls.Count == 0)
            {
                // Fallback: crawl from homepage
                urls = await CrawlLinksAsync(baseUrl, baseUrl, ct);
            }

            logger.LogInformation("Found {Count} URLs to process", urls.Count);

            foreach (var url in urls.Take(MaxPages))
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var result = await ProcessPageAsync(url, ct);
                    if (result.Skipped)
                    {
                        skipped++;
                    }
                    else
                    {
                        processed++;
                        totalChunks += result.ChunkCount;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to process {Url}", url);
                    errors.Add($"{url}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Crawl failed for {BaseUrl}", baseUrl);
            errors.Add($"Crawl failed: {ex.Message}");
        }

        logger.LogInformation("Crawl complete: {Processed} pages, {Skipped} skipped, {Chunks} chunks",
            processed, skipped, totalChunks);

        return new WebsiteCrawlResult(processed, skipped, totalChunks, errors);
    }

    private async Task<List<string>> GetUrlsFromSitemapAsync(string baseUrl, CancellationToken ct)
    {
        var urls = new List<string>();
        var sitemapUrl = baseUrl.TrimEnd('/') + "/sitemap.xml";

        try
        {
            var response = await http.GetAsync(sitemapUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("No sitemap found at {Url}", sitemapUrl);
                return urls;
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            var doc = XDocument.Parse(content);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            foreach (var loc in doc.Descendants(ns + "loc"))
            {
                var url = loc.Value?.Trim();
                if (!string.IsNullOrEmpty(url) && IsValidUrl(url, baseUrl))
                {
                    urls.Add(url);
                }
            }

            logger.LogInformation("Found {Count} URLs in sitemap", urls.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse sitemap");
        }

        return urls;
    }

    private async Task<List<string>> CrawlLinksAsync(string startUrl, string baseUrl, CancellationToken ct)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toVisit = new Queue<string>();
        var urls = new List<string>();

        toVisit.Enqueue(startUrl);
        visited.Add(startUrl);

        while (toVisit.Count > 0 && urls.Count < MaxPages)
        {
            var url = toVisit.Dequeue();
            urls.Add(url);

            try
            {
                var html = await http.GetStringAsync(url, ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var links = doc.DocumentNode.SelectNodes("//a[@href]");
                if (links == null) continue;

                foreach (var link in links)
                {
                    var href = link.GetAttributeValue("href", "");
                    var absoluteUrl = GetAbsoluteUrl(href, url);

                    if (absoluteUrl != null && IsValidUrl(absoluteUrl, baseUrl) && !visited.Contains(absoluteUrl))
                    {
                        visited.Add(absoluteUrl);
                        toVisit.Enqueue(absoluteUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to crawl links from {Url}: {Error}", url, ex.Message);
            }
        }

        return urls;
    }

    private async Task<(bool Skipped, int ChunkCount)> ProcessPageAsync(string url, CancellationToken ct)
    {
        // Skip certain file types
        var matchedExt = SkipExtensions.FirstOrDefault(ext => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        if (matchedExt != null)
        {
            var fileName = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                ? Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath))
                : url;
            logger.LogInformation("SKIPPED (file {Ext}): [{FileName}]", matchedExt, fileName);
            return (true, 0);
        }

        // Check if this is a PDF
        bool isPdf = url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

        // Fetch page/file
        var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var pagePath = Uri.TryCreate(url, UriKind.Absolute, out var uri2)
                ? uri2.AbsolutePath
                : url;
            logger.LogInformation("SKIPPED (HTTP {StatusCode}): {Path}", (int)response.StatusCode, pagePath);
            return (true, 0);
        }

        string title;
        string content;

        if (isPdf)
        {
            // Process PDF
            var pdfBytes = await response.Content.ReadAsByteArrayAsync(ct);
            (title, content) = await ExtractPdfContentAsync(pdfBytes, url, ct);
        }
        else
        {
            // Process HTML
            var html = await response.Content.ReadAsStringAsync(ct);
            (title, content) = ExtractContent(html);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            var reason = isPdf ? "scanned/image PDF - no extractable text" : "no content";
            logger.LogInformation("SKIPPED ({Reason}): [{Title}] {Url}",
                reason, string.IsNullOrEmpty(title) ? "No Title" : title, url);
            return (true, 0);
        }

        if (content.Length < 50)
        {
            logger.LogInformation("SKIPPED (content too short: {Length} chars): [{Title}] {Url}",
                content.Length, string.IsNullOrEmpty(title) ? "No Title" : title, url);
            return (true, 0);
        }

        // Compute hash
        var hash = ComputeHash(content);

        // Check if page already exists with same content
        var existing = await db.CrawledPages.FirstOrDefaultAsync(p => p.Url == url, ct);
        if (existing != null && existing.ContentHash == hash)
        {
            logger.LogInformation("SKIPPED (unchanged): [{Title}] {Url}",
                string.IsNullOrEmpty(title) ? "No Title" : title, url);
            return (true, 0);
        }

        // Delete old chunks if page exists
        if (existing != null)
        {
            await qdrant.DeleteWebsiteChunksAsync(existing.PageId, ct);
            // Also delete old chunks from PostgreSQL
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

        // Create chunks
        var chunks = Chunk(content, ChunkSize, ChunkOverlap)
            .Select((text, idx) => new WebsiteChunk(
                Guid.NewGuid(),
                existing.PageId,
                url,
                title,
                idx,
                text
            ))
            .ToList();

        existing.ChunkCount = chunks.Count;
        await db.SaveChangesAsync(ct);

        // Embed and store chunks in Qdrant
        foreach (var chunk in chunks)
        {
            var vector = await embeddings.EmbedAsync(chunk.Text, ct);
            await qdrant.UpsertWebsiteChunkAsync(chunk, vector, ct);
        }

        // Also store chunks in PostgreSQL so RAG route can look them up by ID
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

        logger.LogInformation("PROCESSED: [{Title}] {Url} - {ChunkCount} chunks",
            string.IsNullOrEmpty(existing.Title) ? "No Title" : existing.Title, url, chunks.Count);
        return (false, chunks.Count);
    }

    private (string Title, string Content) ExtractContent(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Get title first (before removing nodes)
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        var title = titleNode?.InnerText?.Trim() ?? "";

        // Remove scripts, styles, and non-content elements
        // NOTE: Don't remove //form - ASP.NET WebForms wraps entire page in a form tag!
        var nodesToRemove = doc.DocumentNode.SelectNodes(
            "//script|//style|//nav|//footer|//header|//aside|//iframe|//noscript|//meta|//link|//comment()|//input|//select|//button|//textarea");
        if (nodesToRemove != null)
        {
            foreach (var node in nodesToRemove.ToList())
            {
                node.Remove();
            }
        }

        // Try multiple selectors in order of specificity
        string[] contentSelectors =
        {
            "//main",
            "//article",
            "//div[@id='content']",
            "//div[@class='content']",
            "//div[contains(@class, 'content')]",
            "//div[contains(@class, 'main')]",
            "//div[contains(@class, 'page')]",
            "//div[contains(@class, 'post')]",
            "//div[contains(@class, 'entry')]",
            "//div[contains(@class, 'article')]",
            "//div[contains(@id, 'main')]",
            "//div[contains(@id, 'page')]",
            "//section",
            "//body"
        };

        HtmlNode? mainContent = null;
        string usedSelector = "";

        foreach (var selector in contentSelectors)
        {
            mainContent = doc.DocumentNode.SelectSingleNode(selector);
            if (mainContent != null)
            {
                var testText = mainContent.InnerText?.Trim() ?? "";
                // Only use this selector if it has substantial content
                if (Regex.Replace(testText, @"\s+", " ").Trim().Length >= 50)
                {
                    usedSelector = selector;
                    break;
                }
            }
        }

        if (mainContent == null)
        {
            logger.LogDebug("No content container found in HTML");
            return (title, "");
        }

        // Get text content
        var text = mainContent.InnerText ?? "";

        // Clean up whitespace
        text = Regex.Replace(text, @"\s+", " ");
        text = text.Trim();

        logger.LogDebug("Extracted {Length} chars using selector '{Selector}' from page with title '{Title}'",
            text.Length, usedSelector, title);

        return (title, text);
    }

    private async Task<(string Title, string Content)> ExtractPdfContentAsync(byte[] pdfBytes, string url, CancellationToken ct)
    {
        // Extract filename for logging/title fallback
        var fileName = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(uri.AbsolutePath))
            : "PDF Document";

        try
        {
            // Get title from PDF metadata via PdfPig
            var title = fileName;
            try
            {
                using var stream = new MemoryStream(pdfBytes);
                using var document = PdfDocument.Open(stream);

                var pageCount = document.NumberOfPages;
                logger.LogInformation("PDF [{FileName}]: {PageCount} pages, {Size} KB",
                    fileName, pageCount, pdfBytes.Length / 1024);

                var info = document.Information;
                if (!string.IsNullOrWhiteSpace(info.Title))
                    title = info.Title.Trim();
            }
            catch { /* Metadata extraction failed, use filename */ }

            // Always use Vision OCR when available — handles scanned pages, mixed PDFs,
            // and produces better Arabic text than basic PdfPig extraction
            string content;
            if (ocr != null)
            {
                logger.LogInformation("PDF [{FileName}]: Extracting text via vision model (all pages)...", fileName);
                content = await PdfOcrHelper.ExtractTextWithOcrAsync(pdfBytes, ocr, logger, ct);
            }
            else
            {
                // Fallback to PdfPig basic text extraction only if OCR service is unavailable
                logger.LogWarning("PDF [{FileName}]: OCR not available, using basic text extraction", fileName);
                var sb = new StringBuilder();
                using var fallbackStream = new MemoryStream(pdfBytes);
                using var document = PdfDocument.Open(fallbackStream);
                foreach (var page in document.GetPages())
                {
                    var text = page.Text;
                    if (!string.IsNullOrWhiteSpace(text))
                        sb.AppendLine(text);
                }
                content = sb.ToString();
            }

            content = Regex.Replace(content, @"\s+", " ").Trim();

            logger.LogInformation("PDF [{FileName}]: Final extracted content: {Length} chars", fileName, content.Length);
            return (title, content);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PDF [{FileName}]: Failed to process - {Error}", fileName, ex.Message);
            return (fileName, "");
        }
    }

    public static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? GetAbsoluteUrl(string href, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        if (href.StartsWith("#") || href.StartsWith("javascript:") || href.StartsWith("mailto:"))
            return null;

        if (Uri.TryCreate(new Uri(baseUrl), href, out var result))
        {
            // Keep query string — ASP.NET sites use ?id=... as page identifiers
            // Only strip fragments (#section)
            var url = result.GetLeftPart(UriPartial.Query);
            return string.IsNullOrEmpty(url) ? null : url;
        }
        return null;
    }

    private static bool IsValidUrl(string url, string baseUrl)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)) return false;

        // Must be same domain
        if (!uri.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase)) return false;

        // Skip common non-content pages
        var path = uri.AbsolutePath.ToLowerInvariant();
        if (path.Contains("/login") || path.Contains("/logout") ||
            path.Contains("/search") || path.Contains("/admin") ||
            path.Contains("/wp-admin") || path.Contains("/wp-login"))
        {
            return false;
        }

        return true;
    }

    public static IEnumerable<string> Chunk(string text, int chunkSize, int overlap)
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
