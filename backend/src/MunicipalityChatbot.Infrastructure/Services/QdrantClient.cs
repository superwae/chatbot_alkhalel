using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MunicipalityChatbot.Infrastructure.Config;

namespace MunicipalityChatbot.Infrastructure.Services;

public sealed class QdrantClient(HttpClient http, QdrantOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private void ApplyAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            req.Headers.Add("api-key", options.ApiKey);
    }

    public async Task EnsureCollectionAsync(CancellationToken ct)
    {
        // Create collection if missing. If exists, ignore.
        var url = $"{options.Url.TrimEnd('/')}/collections/{options.Collection}";
        using var get = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(get);
        using var getResp = await http.SendAsync(get, ct);
        if (!getResp.IsSuccessStatusCode)
        {
            var createUrl = $"{options.Url.TrimEnd('/')}/collections/{options.Collection}";
            var createBody = new
            {
                vectors = new
                {
                    size = options.VectorSize,
                    distance = "Cosine"
                }
            };
            using var put = new HttpRequestMessage(HttpMethod.Put, createUrl);
            ApplyAuth(put);
            put.Content = new StringContent(JsonSerializer.Serialize(createBody, JsonOptions), Encoding.UTF8, "application/json");
            using var putResp = await http.SendAsync(put, ct);
            putResp.EnsureSuccessStatusCode();
        }

        // Ensure payload indexes exist for filters (Qdrant Cloud can require this).
        await EnsurePayloadIndexAsync("type", "keyword", ct);
        await EnsurePayloadIndexAsync("language", "keyword", ct);
    }

    private async Task EnsurePayloadIndexAsync(string fieldName, string fieldSchema, CancellationToken ct)
    {
        var url = $"{options.Url.TrimEnd('/')}/collections/{options.Collection}/index";
        var body = new { field_name = fieldName, field_schema = fieldSchema };

        using var req = new HttpRequestMessage(HttpMethod.Put, url);
        ApplyAuth(req);
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, ct);
        // If already exists, Qdrant may return 200 or 409 depending on version/config; either is acceptable.
        if (resp.IsSuccessStatusCode) return;

        var txt = await resp.Content.ReadAsStringAsync(ct);
        // Ignore "already exists" style conflicts; throw on other unexpected failures.
        if ((int)resp.StatusCode == 409) return;
        throw new HttpRequestException(
            $"Qdrant payload index creation failed for '{fieldName}': {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {txt}",
            inner: null,
            statusCode: resp.StatusCode);
    }

    public async Task UpsertAsync(IEnumerable<object> points, CancellationToken ct)
    {
        var url = $"{options.Url.TrimEnd('/')}/collections/{options.Collection}/points?wait=true";
        var body = new { points };
        using var req = new HttpRequestMessage(HttpMethod.Put, url);
        ApplyAuth(req);
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<JsonDocument> SearchAsync(object body, CancellationToken ct)
    {
        var url = $"{options.Url.TrimEnd('/')}/collections/{options.Collection}/points/search";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyAuth(req);
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Qdrant search failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {json}",
                inner: null,
                statusCode: resp.StatusCode);
        }
        return JsonDocument.Parse(json);
    }

    public async Task DeleteByFilterAsync(object filter, CancellationToken ct)
    {
        var url = $"{options.Url.TrimEnd('/')}/collections/{options.Collection}/points/delete?wait=true";
        var body = new { filter };
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyAuth(req);
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeleteByIdAsync(string pointId, CancellationToken ct)
    {
        var url = $"{options.Url.TrimEnd('/')}/collections/{options.Collection}/points/delete?wait=true";
        var body = new { points = new[] { pointId } };
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyAuth(req);
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
}

