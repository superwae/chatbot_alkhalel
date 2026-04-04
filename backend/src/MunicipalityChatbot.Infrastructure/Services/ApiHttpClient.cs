using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MunicipalityChatbot.Infrastructure.Config;

namespace MunicipalityChatbot.Infrastructure.Services;

public sealed class ApiHttpClient
{
    private readonly HttpClient _http;
    private readonly ApiCallsOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ApiHttpClient(HttpClient http, ApiCallsOptions options)
    {
        _http = http;
        _options = options;
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<(int statusCode, string body)> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return ((int)resp.StatusCode, body);
    }

    public static HttpContent JsonContent(object value) =>
        new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    public static bool IsRetryableStatus(int status) =>
        status is (int)HttpStatusCode.TooManyRequests or >= 500;
}

