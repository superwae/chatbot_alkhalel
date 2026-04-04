using System.Text.Json;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Application.Models;
using MunicipalityChatbot.Domain.Entities;
using MunicipalityChatbot.Infrastructure.Config;

namespace MunicipalityChatbot.Infrastructure.Services;

public sealed class QdrantService(QdrantClient client, QdrantOptions options) : IQdrantService
{
    public Task EnsureCollectionAsync(int vectorSize, CancellationToken ct)
    {
        // options.VectorSize already used in client; keep param for compatibility.
        return client.EnsureCollectionAsync(ct);
    }

    public Task UpsertFaqAsync(Faq faq, float[] vector, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "faq",
            ["faqId"] = faq.FaqId.ToString(),
            ["language"] = faq.Language,
            ["title"] = faq.Title,
            ["question"] = faq.Question,
            ["shortDescription"] = faq.ShortDescription,
            ["tags"] = faq.TagsCsv,
            ["department"] = faq.Department,
            ["isActive"] = faq.IsActive
        };

        var point = new
        {
            id = faq.FaqId.ToString(),
            vector,
            payload
        };
        return client.UpsertAsync([point], ct);
    }

    public Task UpsertDocChunkAsync(DocumentChunk chunk, float[] vector, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "doc_chunk",
            ["chunkId"] = chunk.ChunkId.ToString(),
            ["docId"] = chunk.DocId.ToString(),
            ["filename"] = chunk.Filename,
            ["filetype"] = chunk.FileType,
            ["language"] = chunk.Language,
            ["page"] = chunk.PageNumber,
            ["sheet"] = chunk.SheetName,
            ["chunkIndex"] = chunk.ChunkIndex,
            ["text"] = chunk.Text  // IMPORTANT: Store text so planner can see content
        };

        var point = new
        {
            id = chunk.ChunkId.ToString(),
            vector,
            payload
        };
        return client.UpsertAsync([point], ct);
    }

    public async Task<IReadOnlyList<QdrantPoint>> SearchAsync(string type, float[] vector, int topK, string? language, CancellationToken ct)
    {
        var filterMust = new List<object>
        {
            new { key = "type", match = new { value = type } }
        };
        if (!string.IsNullOrWhiteSpace(language))
            filterMust.Add(new { key = "language", match = new { value = language } });

        var body = new
        {
            vector,
            limit = topK,
            with_payload = true,
            filter = new { must = filterMust }
        };

        using var json = await client.SearchAsync(body, ct);
        var result = json.RootElement.GetProperty("result");
        var points = new List<QdrantPoint>();

        foreach (var item in result.EnumerateArray())
        {
            var id = item.GetProperty("id").ToString();
            var score = item.GetProperty("score").GetDouble();
            var payload = new Dictionary<string, object>();
            if (item.TryGetProperty("payload", out var p))
            {
                foreach (var prop in p.EnumerateObject())
                    payload[prop.Name] = JsonElementToObject(prop.Value);
            }
            points.Add(new QdrantPoint(id, score, payload));
        }

        return points;
    }

    public Task DeleteDocumentAsync(Guid docId, CancellationToken ct)
    {
        var filter = new
        {
            must = new[]
            {
                new { key = "docId", match = new { value = docId.ToString() } }
            }
        };
        return client.DeleteByFilterAsync(filter, ct);
    }

    public Task DeleteFaqAsync(Guid faqId, CancellationToken ct)
    {
        return client.DeleteByIdAsync(faqId.ToString(), ct);
    }

    public Task UpsertWebsiteChunkAsync(WebsiteChunk chunk, float[] vector, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "website",
            ["chunkId"] = chunk.ChunkId.ToString(),
            ["pageId"] = chunk.PageId.ToString(),
            ["url"] = chunk.Url,
            ["title"] = chunk.Title,
            ["chunkIndex"] = chunk.ChunkIndex,
            ["text"] = chunk.Text
        };

        var point = new
        {
            id = chunk.ChunkId.ToString(),
            vector,
            payload
        };
        return client.UpsertAsync([point], ct);
    }

    public Task DeleteWebsiteChunksAsync(Guid pageId, CancellationToken ct)
    {
        var filter = new
        {
            must = new object[]
            {
                new { key = "type", match = new { value = "website" } },
                new { key = "pageId", match = new { value = pageId.ToString() } }
            }
        };
        return client.DeleteByFilterAsync(filter, ct);
    }

    public Task DeleteAllWebsiteChunksAsync(CancellationToken ct)
    {
        var filter = new
        {
            must = new object[]
            {
                new { key = "type", match = new { value = "website" } }
            }
        };
        return client.DeleteByFilterAsync(filter, ct);
    }

    private static object JsonElementToObject(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? "",
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => "",
            JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(),
            _ => el.ToString()
        };
}

