using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Application.Models;
using MunicipalityChatbot.Infrastructure.Config;

namespace MunicipalityChatbot.Infrastructure.Services;

public sealed class GeminiClient(
    HttpClient http,
    GeminiOptions options,
    ILogger<GeminiClient> logger
) : IEmbeddingService, IPlanningService, IRagAnswerService, IApiAnswerService, IGeneralAnswerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        // v1beta: POST {baseUrl}/models/{model}:embedContent?key=API_KEY
        var url = $"{options.BaseUrl.TrimEnd('/')}/models/{options.EmbeddingModel}:embedContent?key={Uri.EscapeDataString(options.ApiKey)}";
        var body = new
        {
            content = new
            {
                parts = new object[] { new { text } }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("embedding").GetProperty("values").EnumerateArray()
            .Select(x => (float)x.GetDouble()).ToArray();
        logger.LogInformation("Gemini embedding vector length: {Length}", values.Length);
        return values;
    }

    public async Task<PlannerResult> PlanAsync(PlannerInput input, CancellationToken ct)
    {
        var prompt = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "prompts", "classify_and_plan.prompt.txt"), ct);
        prompt = prompt.Replace("{{COMPLAINT_CATEGORIES}}", BuildComplaintCategoriesText(input.ApiDefinitions));
        var system = prompt;
        var user = JsonSerializer.Serialize(new
        {
            userMessage = input.UserMessage,
            userLang = input.UserLang,
            faqCandidates = input.FaqCandidates,
            docChunkCandidates = input.DocChunkCandidates,
            apiDefinitions = input.ApiDefinitions,
            sessionState = input.SessionState,
            conversationHistory = input.ConversationHistory
        }, JsonOptions);

        var plannerModel = !string.IsNullOrWhiteSpace(options.PlannerModel) ? options.PlannerModel : null;
        logger.LogInformation("Planner using model: {Model}", plannerModel ?? options.Model);
        var raw = await GenerateContentAsync(system, user, null, ct, modelOverride: plannerModel);
        raw = ExtractJsonIfWrapped(raw);

        try
        {
            var parsed = JsonSerializer.Deserialize<PlannerResult>(raw, JsonOptions);
            if (parsed is null) return FallbackPlanner(raw);
            parsed.RawJson = raw;
            return parsed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Planner returned invalid JSON. Falling back.");
            return FallbackPlanner(raw);
        }
    }

    public async Task<string> AnswerFromChunksAsync(string userMessage, string userLang, IReadOnlyList<MunicipalityChatbot.Domain.Entities.DocumentChunk> chunks, IReadOnlyList<ConversationMessage>? conversationHistory, CancellationToken ct)
    {
        var prompt = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "prompts", "rag_answer.prompt.txt"), ct);
        var system = prompt;
        var user = JsonSerializer.Serialize(new
        {
            userMessage,
            userLang,
            selectedChunks = chunks.Select(c => new
            {
                chunkId = c.ChunkId,
                filename = c.Filename,
                page = c.PageNumber,
                sheet = c.SheetName,
                text = c.Text
            })
        }, JsonOptions);

        return await GenerateContentAsync(system, user, conversationHistory, ct);
    }

    public async Task<string> AnswerFromApiResultAsync(string userMessage, string userLang, string apiName, string apiResultJson, string notes, IReadOnlyList<ConversationMessage>? conversationHistory, CancellationToken ct)
    {
        var prompt = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "prompts", "api_answer.prompt.txt"), ct);
        var system = prompt;
        var user = JsonSerializer.Serialize(new
        {
            userMessage,
            userLang,
            apiName,
            apiResult = JsonDocument.Parse(apiResultJson).RootElement,
            notes
        }, JsonOptions);

        return await GenerateContentAsync(system, user, conversationHistory, ct);
    }

    public async Task<string> AnswerGeneralAsync(string userMessage, string userLang, IReadOnlyList<ConversationMessage>? conversationHistory, CancellationToken ct)
    {
        var system = GetGeneralSystemPrompt(userLang);
        return await GenerateContentAsync(system, userMessage, conversationHistory, ct);
    }

    public async IAsyncEnumerable<string> StreamAnswerGeneralAsync(string userMessage, string userLang, IReadOnlyList<ConversationMessage>? conversationHistory, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Gemini streaming fallback: yield full response at once
        var result = await AnswerGeneralAsync(userMessage, userLang, conversationHistory, ct);
        yield return result;
    }

    public async IAsyncEnumerable<string> StreamAnswerFromChunksAsync(string userMessage, string userLang, IReadOnlyList<MunicipalityChatbot.Domain.Entities.DocumentChunk> chunks, IReadOnlyList<ConversationMessage>? conversationHistory, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Gemini streaming fallback: yield full response at once
        var result = await AnswerFromChunksAsync(userMessage, userLang, chunks, conversationHistory, ct);
        yield return result;
    }

    private static string GetGeneralSystemPrompt(string userLang) => userLang == "ar"
        ? "أنت خليلي - المساعد الآلي الرسمي لبلدية الخليل. عند التعريف بنفسك، قل \"أنا خليلي\". استخدم \"نحن\" عند الحديث عن البلدية. كن إيجابياً ومرحباً."
        : "You are Khalili (خليلي) - the official Hebron Municipality chatbot. When introducing yourself, say \"I'm Khalili\". Use \"we\" when referring to the municipality. Be positive and welcoming.";

    private async Task<string> GenerateContentAsync(string systemPrompt, string userContent, IReadOnlyList<ConversationMessage>? conversationHistory, CancellationToken ct, string? modelOverride = null)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("Gemini API key is missing. Set GEMINI__API_KEY (or Gemini:ApiKey).");

        var model = modelOverride ?? options.Model;
        // v1beta: POST {baseUrl}/models/{model}:generateContent?key=API_KEY
        var url = $"{options.BaseUrl.TrimEnd('/')}/models/{model}:generateContent?key={Uri.EscapeDataString(options.ApiKey)}";
        
        var contents = BuildGeminiContentsArray(userContent, conversationHistory);
        
        var reqBody = new
        {
            systemInstruction = new
            {
                parts = new object[] { new { text = systemPrompt } }
            },
            contents = contents,
            generationConfig = new
            {
                temperature = 0.2
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(reqBody, JsonOptions), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            return "";

        var first = candidates[0];
        if (!first.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0)
            return "";

        var text = parts[0].GetProperty("text").GetString();
        return text?.Trim() ?? "";
    }

    /// <summary>
    /// Builds the contents array for Gemini API calls, including conversation history.
    /// </summary>
    private static object[] BuildGeminiContentsArray(string userContent, IReadOnlyList<ConversationMessage>? conversationHistory)
    {
        var contents = new List<object>();

        // Add conversation history (if available) before the current user message
        if (conversationHistory is not null)
        {
            foreach (var msg in conversationHistory)
            {
                // Gemini uses "model" instead of "assistant"
                var role = msg.Role == "assistant" ? "model" : msg.Role;
                contents.Add(new
                {
                    role = role,
                    parts = new object[] { new { text = msg.Text } }
                });
            }
        }

        // Add the current user message
        contents.Add(new
        {
            role = "user",
            parts = new object[] { new { text = userContent } }
        });

        return contents.ToArray();
    }

    private static string ExtractJsonIfWrapped(string raw)
    {
        // If the model returns ```json ... ``` or ``` ... ```, extract the fenced content.
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim();
        if (!s.Contains("```", StringComparison.Ordinal)) return s;

        var firstFence = s.IndexOf("```", StringComparison.Ordinal);
        if (firstFence < 0) return s;
        var afterFirst = s.IndexOf('\n', firstFence);
        if (afterFirst < 0) return s;
        var secondFence = s.IndexOf("```", afterFirst + 1, StringComparison.Ordinal);
        if (secondFence < 0) return s;

        var inner = s.Substring(afterFirst + 1, secondFence - (afterFirst + 1));
        return inner.Trim();
    }

    private static PlannerResult FallbackPlanner(string raw) =>
        new()
        {
            Route = "GENERAL",
            Confidence = 0,
            FinalAnswerStyle = "short, clear, municipality-friendly",
            RawJson = raw
        };

    private static string BuildComplaintCategoriesText(IReadOnlyList<object> apiDefinitions)
    {
        try
        {
            var json = JsonSerializer.Serialize(apiDefinitions, JsonOptions);
            using var doc = JsonDocument.Parse(json);
            foreach (var api in doc.RootElement.EnumerateArray())
            {
                if (!api.TryGetProperty("method", out var methodProp)) continue;
                if (!methodProp.GetString()!.Equals("POST", StringComparison.OrdinalIgnoreCase)) continue;
                if (!api.TryGetProperty("bodySchema", out var schemaProp)) continue;

                var schemaJson = schemaProp.GetString() ?? schemaProp.GetRawText();
                using var schema = JsonDocument.Parse(schemaJson);
                if (!schema.RootElement.TryGetProperty("CATEGORY_SUB_ID", out var catProp)) continue;
                if (!catProp.TryGetProperty("description", out var descProp)) continue;

                var desc = descProp.GetString() ?? "";
                var lines = new List<string>();
                foreach (Match m in Regex.Matches(desc, @"(\d+)=([^,]+)"))
                    lines.Add($"     {m.Groups[1].Value} = {m.Groups[2].Value.Trim()}");

                if (lines.Count > 0)
                    return string.Join("\n", lines);
            }
        }
        catch { }
        return "     (categories unavailable — ask user to describe the problem type)";
    }
}

