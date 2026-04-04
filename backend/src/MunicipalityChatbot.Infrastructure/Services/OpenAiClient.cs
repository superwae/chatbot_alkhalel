using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Application.Models;
using MunicipalityChatbot.Infrastructure.Config;

namespace MunicipalityChatbot.Infrastructure.Services;

public sealed class OpenAiClient(
    HttpClient http,
    OpenAiOptions options,
    ILogger<OpenAiClient> logger
) : IEmbeddingService, IPlanningService, IRagAnswerService, IApiAnswerService, IGeneralAnswerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var req = new
        {
            model = options.EmbeddingModel,
            input = text
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/embeddings");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(req, JsonOptions), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(message, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var vec = doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray()
            .Select(x => (float)x.GetDouble()).ToArray();
        return vec;
    }

    public async Task<PlannerResult> PlanAsync(PlannerInput input, CancellationToken ct)
    {
        var prompt = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "prompts", "classify_and_plan.prompt.txt"), ct);
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
        var raw = await ChatCompletionAsync(system, user, ct, modelOverride: plannerModel);
        var result = new PlannerResult { RawJson = raw };

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

    public async Task<string> AnswerFromChunksAsync(string userMessage, string userLang, IReadOnlyList<MunicipalityChatbot.Domain.Entities.DocumentChunk> chunks, CancellationToken ct)
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

        return await ChatCompletionAsync(system, user, ct);
    }

    public async Task<string> AnswerFromApiResultAsync(string userMessage, string userLang, string apiName, string apiResultJson, string notes, CancellationToken ct)
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

        return await ChatCompletionAsync(system, user, ct);
    }

    public async Task<string> AnswerGeneralAsync(string userMessage, string userLang, CancellationToken ct)
    {
        var system = GetGeneralSystemPrompt(userLang);
        return await ChatCompletionAsync(system, userMessage, ct);
    }

    public async IAsyncEnumerable<string> StreamAnswerGeneralAsync(string userMessage, string userLang, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var system = GetGeneralSystemPrompt(userLang);
        await foreach (var chunk in StreamChatCompletionAsync(system, userMessage, ct))
        {
            yield return chunk;
        }
    }

    public async IAsyncEnumerable<string> StreamAnswerFromChunksAsync(string userMessage, string userLang, IReadOnlyList<MunicipalityChatbot.Domain.Entities.DocumentChunk> chunks, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var prompt = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "prompts", "rag_answer.prompt.txt"), ct);
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

        await foreach (var chunk in StreamChatCompletionAsync(prompt, user, ct))
        {
            yield return chunk;
        }
    }

    private static string GetGeneralSystemPrompt(string userLang) => userLang == "ar"
        ? """
أنت خليلي - المساعد الآلي الرسمي لبلدية الخليل. تتحدث بصفتك ممثلاً رسمياً للبلدية.
استخدم صيغة "نحن" عند الحديث عن البلدية (نحن في البلدية، خدماتنا، يمكنك التواصل معنا).

للتحيات (مرحبا، أهلاً، السلام عليكم، إلخ):
- رد بتحية دافئة: "أهلاً وسهلاً! أنا خليلي. كيف يمكنني مساعدتك؟"
- لا تضف أي تنبيهات أو إخلاء مسؤولية

⚠️ لا تقل "أنا خليلي" في كل رد - فقط عند التحية الأولى أو إذا سأل المستخدم "من أنت". في باقي الردود أجب مباشرة بدون تقديم نفسك.

🔗 الروابط: إذا كان الجواب يتعلق بصفحة أو خدمة على موقع البلدية، أضف الرابط المناسب إذا كنت تعرفه. مثال: https://www.hebron-city.ps

⚠️ خدمات خارج نطاق البلدية - ارفض بأدب:
إذا سأل المستخدم عن خدمات حكومية ليست من اختصاصنا، مثل:
- رخصة القيادة، تجديد الرخصة، السياقة
- جواز السفر، الهوية، الأحوال المدنية
- التأمين الصحي، الضمان الاجتماعي
- الضرائب (غير رسوم البلدية)
- المحاكم، الشرطة، الجهات الأمنية
- وزارة التربية، المدارس الحكومية

→ أجب: "عذراً، هذه الخدمة ليست من اختصاصنا في البلدية. يرجى التواصل مع الجهة المختصة. هل يمكنني مساعدتك بشيء آخر؟"

⚠️ أسئلة غير متعلقة بالبلدية (مطاعم، أماكن عامة، إلخ):
→ أجب: "أنا خليلي، هنا لمساعدتك في خدمات البلدية. كيف يمكنني مساعدتك في خدماتنا؟"

✅ أجب على:
- التحيات والمجاملات
- أسئلة عن خدمات البلدية

كن دائماً إيجابياً ومرحباً، وتذكر أنك تمثل البلدية مباشرة.
"""
        : """
You are Khalili (خليلي) - the official Hebron Municipality chatbot. You speak as a direct representative of the municipality.
Use "we" when referring to the municipality (we at the municipality, our services, contact us).

For greetings (hi, hello, hey, etc.):
- Respond: "Hello! I'm Khalili. How can I help you?"
- Do NOT add any disclaimers or warnings

⚠️ Do NOT say "I'm Khalili" in every response - only on the first greeting or if the user asks "who are you". In all other responses, answer directly without introducing yourself.

🔗 Links: If your answer relates to a page or service on the municipality website, include the relevant link if you know it. Example: https://www.hebron-city.ps

⚠️ OUT OF SCOPE - Politely decline:
If the user asks about government services NOT provided by us, such as:
- Driver's license, driving tests, vehicle registration
- Passport, national ID, civil affairs
- Health insurance, social security
- Taxes (other than municipal fees)
- Courts, police, security services
- Ministry of Education, public schools

→ Respond: "I'm sorry, this service is not provided by us at the municipality. Please contact the relevant authority. Is there anything else I can help you with?"

⚠️ Non-municipality questions (restaurants, general places, etc.):
→ Respond: "I'm Khalili, here to help you with municipality services. How can I help you with our services?"

✅ DO answer:
- Greetings and pleasantries
- Questions about municipality services

Always be positive and welcoming, and remember you represent the municipality directly.
""";

    private async Task<string> ChatCompletionAsync(string systemPrompt, string userContent, CancellationToken ct, string? modelOverride = null)
    {
        var model = modelOverride ?? options.Model;
        var reqBody = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            }
        };

        // GPT-5+ models don't support temperature parameter (only temperature=1 allowed)
        if (!IsGpt5Model(model))
            reqBody["temperature"] = 0.2;

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/chat/completions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(reqBody, JsonOptions), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(message, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return content?.Trim() ?? "";
    }

    private async IAsyncEnumerable<string> StreamChatCompletionAsync(string systemPrompt, string userContent, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var reqBody = new Dictionary<string, object>
        {
            ["model"] = options.Model,
            ["stream"] = true,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            }
        };

        // GPT-5+ models don't support temperature parameter
        if (!IsGpt5Model(options.Model))
            reqBody["temperature"] = 0.2;

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/chat/completions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(reqBody, JsonOptions), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            string? chunk = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var delta = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("delta");

                if (delta.TryGetProperty("content", out var contentEl))
                {
                    chunk = contentEl.GetString();
                }
            }
            catch
            {
                // Skip malformed lines
            }

            if (!string.IsNullOrEmpty(chunk))
                yield return chunk;
        }
    }

    /// <summary>
    /// GPT-5+ models don't support the temperature parameter (only temperature=1 allowed).
    /// Detect model family to conditionally omit temperature from requests.
    /// </summary>
    private static bool IsGpt5Model(string model) =>
        model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
        model.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
        model.StartsWith("o4", StringComparison.OrdinalIgnoreCase);

    private static PlannerResult FallbackPlanner(string raw) =>
        new()
        {
            Route = "GENERAL",
            Confidence = 0,
            FinalAnswerStyle = "short, clear, municipality-friendly",
            RawJson = raw
        };
}

