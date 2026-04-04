using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Infrastructure.Config;

namespace MunicipalityChatbot.Infrastructure.Services;

public sealed class VisionOcrService(
    HttpClient http,
    OpenAiOptions options,
    ILogger<VisionOcrService> logger
) : IOcrService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private const string OcrPrompt = """
        Extract ALL text from this scanned document image exactly as it appears.
        Rules:
        - Preserve the original language (Arabic, English, or mixed).
        - Maintain paragraph structure and line breaks.
        - For tables, output each row on its own line with columns separated by " | ".
        - Include page headers, footers, and article numbers.
        - Do NOT add any commentary, translation, or explanation — output ONLY the extracted text.
        """;

    public async Task<string> ExtractTextFromImageAsync(byte[] imageBytes, CancellationToken ct)
    {
        var base64 = Convert.ToBase64String(imageBytes);
        var dataUrl = $"data:image/png;base64,{base64}";

        var model = options.VisionModel;

        var reqBody = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = OcrPrompt },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = dataUrl, detail = "high" }
                        }
                    }
                }
            }
        };

        // GPT-5+ models use max_completion_tokens; older models use max_tokens
        if (IsGpt5Model(model))
            reqBody["max_completion_tokens"] = 4096;
        else
            reqBody["max_tokens"] = 4096;

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/chat/completions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(reqBody, JsonOptions), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(message, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Vision OCR API error ({StatusCode}): {Body}", (int)resp.StatusCode, body.Length > 500 ? body[..500] : body);
            return "";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return content?.Trim() ?? "";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse vision OCR response");
            return "";
        }
    }

    private static bool IsGpt5Model(string model) =>
        model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
        model.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
        model.StartsWith("o4", StringComparison.OrdinalIgnoreCase);
}
