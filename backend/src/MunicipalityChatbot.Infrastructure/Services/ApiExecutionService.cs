using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Application.Models;
using MunicipalityChatbot.Domain.Entities;
using MunicipalityChatbot.Infrastructure.Config;

namespace MunicipalityChatbot.Infrastructure.Services;

public sealed class ApiExecutionService(
    ApiHttpClient http,
    ApiCallsOptions options,
    IConfiguration config,
    ILogger<ApiExecutionService> logger
) : IApiExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApiExecutionResult> ExecuteAsync(ApiDefinition api, PlannerApiCall apiCall, CancellationToken ct, string? userToken = null)
    {
        // Guardrails: allowlisted domain must match baseUrl host
        if (!Uri.TryCreate(api.BaseUrl, UriKind.Absolute, out var baseUri))
            return new ApiExecutionResult(false, null, "", "Invalid API baseUrl.", api.BaseUrl);

        if (!string.Equals(baseUri.Host, api.AllowlistedDomain, StringComparison.OrdinalIgnoreCase))
            return new ApiExecutionResult(false, null, "", "API baseUrl host is not allowlisted.", api.BaseUrl);

        // Guardrails: only allow calling the saved apiId
        if (!Guid.TryParse(apiCall.ApiId, out var apiId) || apiId != api.ApiId)
            return new ApiExecutionResult(false, null, "", "API call is not allowed.", api.BaseUrl);

        // Build URL: base + pathTemplate with {vars} from params.path
        var path = api.PathTemplate ?? "";
        var pathVars = apiCall.Params.Path ?? new Dictionary<string, object>();
        foreach (var kv in pathVars)
            path = path.Replace("{" + kv.Key + "}", Uri.EscapeDataString(kv.Value?.ToString() ?? ""), StringComparison.OrdinalIgnoreCase);

        var uriBuilder = new UriBuilder(new Uri(baseUri, path));

        // Query params (no System.Web in ASP.NET Core; build safely)
        var q = apiCall.Params.Query ?? new Dictionary<string, object>();
        if (q.Count > 0)
        {
            var pairs = q
                .Where(kv => kv.Value is not null)
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!.ToString() ?? "")}");
            uriBuilder.Query = string.Join("&", pairs);
        }

        var method = new HttpMethod(api.Method.ToUpperInvariant());
        var requestBody = apiCall.Params.Body ?? new Dictionary<string, object>();

        // Helper to create a fresh request for each attempt (HttpRequestMessage can't be reused)
        HttpRequestMessage CreateRequest()
        {
            var req = new HttpRequestMessage(method, uriBuilder.Uri);

            // Headers template + planner headers
            ApplyHeaders(req, api.HeadersTemplateJson);
            if (apiCall.Params.Headers is { Count: > 0 })
                foreach (var kv in apiCall.Params.Headers)
                    TryAddHeader(req, kv.Key, kv.Value?.ToString());

            // Auth
            ApplyAuth(req, api, userToken);

            // Body (POST/PUT/PATCH)
            if (requestBody.Count > 0)
                req.Content = new StringContent(
                    JsonSerializer.Serialize(requestBody, JsonOptions),
                    System.Text.Encoding.UTF8,
                    "application/json");

            return req;
        }

        // Execute with minimal retry policy (simple loop)
        var attempts = 0;
        Exception? lastEx = null;
        HttpRequestMessage? lastRequest = null;
        while (attempts <= options.MaxRetries)
        {
            try
            {
                lastRequest = CreateRequest();
                using var req = lastRequest;
                var (status, body) = await http.SendAsync(req, ct);
                if (attempts < options.MaxRetries && ApiHttpClient.IsRetryableStatus(status))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempts + 1)), ct);
                    attempts++;
                    continue;
                }

                // Save successful API response to file
                if (status is >= 200 and < 300)
                {
                    await SaveApiResponseToFileAsync(api.ApiName, uriBuilder.Uri.ToString(), status, body, ct);
                }

                return new ApiExecutionResult(status is >= 200 and < 300, status, body, status is >= 200 and < 300 ? null : $"API returned status {status}", uriBuilder.Uri.ToString());
            }
            catch (Exception ex) when (attempts < options.MaxRetries)
            {
                lastEx = ex;
                attempts++;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempts), ct);
            }
            catch (Exception ex)
            {
                lastEx = ex;
                if (lastRequest is not null)
                {
                    logger.LogWarning(ex, "API execution failed after retries. URL={Url}. Request headers: {Headers}", uriBuilder.Uri, FormatRequestHeaders(lastRequest));
                }
                break;
            }
        }

        logger.LogWarning(lastEx, "API execution failed after retries.");
        return new ApiExecutionResult(false, null, "", lastEx?.Message ?? "API execution failed.", uriBuilder.Uri.ToString());
    }

    private void ApplyHeaders(HttpRequestMessage req, string headersTemplateJson)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(headersTemplateJson, JsonOptions);
            if (dict is null) return;
            foreach (var kv in dict)
                TryAddHeader(req, kv.Key, kv.Value);
        }
        catch
        {
            // ignore malformed template
        }
    }

    private void TryAddHeader(HttpRequestMessage req, string headerName, string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerName) || headerValue is null)
            return;

        if (!IsAscii(headerName) || !IsAscii(headerValue))
        {
            logger.LogWarning("Skipping invalid header because it contains non-ASCII characters: {HeaderName}={HeaderValue}", headerName, headerValue);
            return;
        }

        try
        {
            req.Headers.TryAddWithoutValidation(headerName, headerValue);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to add API header {HeaderName}={HeaderValue}", headerName, headerValue);
        }
    }

    private static string FormatRequestHeaders(HttpRequestMessage req)
    {
        var allHeaders = req.Headers
            .Concat(req.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}");

        return string.Join("\n", allHeaders);
    }

    private static bool IsAscii(string? value)
        => !string.IsNullOrEmpty(value) && value.All(c => c <= 127);

    private void ApplyAuth(HttpRequestMessage req, ApiDefinition api, string? userToken = null)
    {
        // authConfigJson holds env var names, not secrets
        var authType = api.AuthType ?? "None";
        var authConfig = ParseStringDict(api.AuthConfigJson);

        if (authType.Equals("UserToken", StringComparison.OrdinalIgnoreCase))
        {
            // Use the citizen's own token forwarded from the widget/frontend
            if (!string.IsNullOrWhiteSpace(userToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        }
        else if (authType.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            var headerName = authConfig.GetValueOrDefault("headerName") ?? "X-API-Key";
            var envVar = authConfig.GetValueOrDefault("apiKeyEnvVar");
            var value = ResolveEnv(envVar);
            if (!string.IsNullOrWhiteSpace(value))
                TryAddHeader(req, headerName, value);
        }
        else if (authType.Equals("BearerToken", StringComparison.OrdinalIgnoreCase))
        {
            var envVar = authConfig.GetValueOrDefault("tokenEnvVar");
            var value = ResolveEnv(envVar);
            if (!string.IsNullOrWhiteSpace(value))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
        }
        else if (authType.Equals("Basic", StringComparison.OrdinalIgnoreCase))
        {
            var userEnv = authConfig.GetValueOrDefault("usernameEnvVar");
            var passEnv = authConfig.GetValueOrDefault("passwordEnvVar");
            var user = ResolveEnv(userEnv);
            var pass = ResolveEnv(passEnv);
            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
            {
                var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{pass}"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
        }
    }

    private string? ResolveEnv(string? envVarName)
    {
        if (string.IsNullOrWhiteSpace(envVarName)) return null;
        // prefer IConfiguration so docker env var mapping works
        return config[envVarName] ?? Environment.GetEnvironmentVariable(envVarName);
    }

    private static Dictionary<string, string?> ParseStringDict(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions) ?? new Dictionary<string, string?>();
        }
        catch
        {
            return new Dictionary<string, string?>();
        }
    }

    private async Task SaveApiResponseToFileAsync(string apiName, string url, int? statusCode, string responseBody, CancellationToken ct)
    {
        try
        {
            // Create api_responses directory if it doesn't exist
            var directory = Path.Combine(AppContext.BaseDirectory, "api_responses");
            Directory.CreateDirectory(directory);

            // Generate filename with timestamp and API name
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff");
            var safeApiName = apiName.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
            var filename = $"{timestamp}_{safeApiName}_{statusCode}.json";
            var filePath = Path.Combine(directory, filename);

            // Create response data object
            var responseData = new
            {
                Timestamp = DateTime.UtcNow,
                ApiName = apiName,
                Url = url,
                StatusCode = statusCode,
                ResponseBody = responseBody
            };

            // Serialize and save to file
            var json = JsonSerializer.Serialize(responseData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json, ct);

            logger.LogInformation("Saved API response to file: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save API response to file for API: {ApiName}", apiName);
        }
    }
}

