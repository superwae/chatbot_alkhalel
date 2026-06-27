using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Domain.Entities;

namespace MunicipalityChatbot.Api.Controllers;

[ApiController]
[Route("api/documents")]
public sealed class DocumentsController(
    IDocumentRepository repo,
    IIngestionService ingestion,
    IQdrantService qdrant,
    IEmbeddingService embeddings,
    ILogger<DocumentsController> logger
) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin},{EmployeeRoles.EmployeeEditor}")]
    public async Task<ActionResult<IReadOnlyList<Document>>> List(CancellationToken ct)
        => Ok(await repo.ListDocsAsync(200, ct));

    [HttpPost("upload")]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin},{EmployeeRoles.EmployeeEditor}")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult> Upload([FromForm] IFormFile file, [FromForm] string? language, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("File is required.");

        var ext = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pdf", "doc", "docx", "xlsx", "txt", "md", "png", "jpg", "jpeg" };
        if (!allowed.Contains(ext)) return BadRequest($"Unsupported file type: .{ext}. Allowed: {string.Join(", ", allowed)}");

        // Delete any existing documents with the same filename (re-upload replaces old version)
        var existing = await repo.FindByFilenameAsync(file.FileName, ct);
        if (existing.Count > 0)
        {
            logger.LogInformation("Re-upload detected for '{Filename}': deleting {Count} old version(s)", file.FileName, existing.Count);
            foreach (var old in existing)
            {
                await qdrant.DeleteDocumentAsync(old.DocId, ct);
                await repo.DeleteDocAsync(old.DocId, ct);
            }
        }

        var doc = new Document
        {
            DocId = Guid.NewGuid(),
            Filename = file.FileName,
            FileType = ext,
            FileSizeBytes = file.Length,
            DetectedLanguage = string.IsNullOrWhiteSpace(language) ? null : language.Trim().ToUpperInvariant(),
            IsActive = true
        };

        var docId = await repo.CreateDocAsync(doc, ct);
        await using var stream = file.OpenReadStream();
        await ingestion.IngestDocumentAsync(docId, doc.Filename, doc.FileType, stream, ct);

        return Ok(new { docId, replaced = existing.Count > 0 });
    }

    [HttpDelete("{docId:guid}")]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin},{EmployeeRoles.EmployeeEditor}")]
    public async Task<ActionResult> Delete(Guid docId, CancellationToken ct)
    {
        // Delete from Qdrant (all chunks with this docId)
        await qdrant.DeleteDocumentAsync(docId, ct);

        // Delete from PostgreSQL (document and all chunks)
        await repo.DeleteDocAsync(docId, ct);

        return NoContent();
    }

    /// <summary>
    /// Re-embeds all chunks from PostgreSQL to Qdrant.
    /// Useful when Qdrant is missing embeddings that exist in the database.
    /// </summary>
    [HttpPost("reindex")]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin}")]
    public async Task<ActionResult> Reindex(CancellationToken ct)
    {
        var chunks = await repo.ListAllChunksAsync(ct);
        if (chunks.Count == 0)
            return Ok(new { message = "No chunks found to reindex.", count = 0 });

        var indexed = 0;
        foreach (var chunk in chunks)
        {
            try
            {
                var vec = await embeddings.EmbedAsync(chunk.Text, ct);
                await qdrant.UpsertDocChunkAsync(chunk, vec, ct);
                indexed++;
            }
            catch
            {
                // Continue with other chunks if one fails
            }
        }

        return Ok(new { message = $"Reindexed {indexed} of {chunks.Count} chunks to Qdrant.", count = indexed });
    }
}

