using Microsoft.EntityFrameworkCore;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Domain.Entities;
using MunicipalityChatbot.Infrastructure.Db;

namespace MunicipalityChatbot.Infrastructure.Repositories;

public sealed class DocumentRepository(AppDbContext db) : IDocumentRepository
{
    public async Task<Document?> GetDocAsync(Guid docId, CancellationToken ct)
    {
        return await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.DocId == docId, ct);
    }

    public async Task<IReadOnlyList<Document>> FindByFilenameAsync(string filename, CancellationToken ct)
    {
        return await db.Documents.AsNoTracking()
            .Where(x => x.Filename == filename)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateDocAsync(Document doc, CancellationToken ct)
    {
        doc.DocId = doc.DocId == Guid.Empty ? Guid.NewGuid() : doc.DocId;
        doc.CreatedAt = DateTimeOffset.UtcNow;
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
        return doc.DocId;
    }

    public async Task<IReadOnlyList<Document>> ListDocsAsync(int limit, CancellationToken ct)
    {
        return await db.Documents.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task AddChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var c in chunks)
        {
            if (c.ChunkId == Guid.Empty) c.ChunkId = Guid.NewGuid();
            c.CreatedAt = now;
        }
        db.DocumentChunks.AddRange(chunks);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentChunk>> GetChunksByIdsAsync(IEnumerable<Guid> chunkIds, CancellationToken ct)
    {
        var ids = chunkIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await db.DocumentChunks.AsNoTracking()
            .Where(x => ids.Contains(x.ChunkId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentChunk>> ListAllChunksAsync(CancellationToken ct)
    {
        return await db.DocumentChunks.AsNoTracking()
            .OrderBy(x => x.DocId)
            .ThenBy(x => x.ChunkIndex)
            .ToListAsync(ct);
    }

    public async Task DeleteDocAsync(Guid docId, CancellationToken ct)
    {
        // Delete all chunks for this document
        await db.DocumentChunks.Where(x => x.DocId == docId).ExecuteDeleteAsync(ct);

        // Delete the document itself
        await db.Documents.Where(x => x.DocId == docId).ExecuteDeleteAsync(ct);
    }
}

