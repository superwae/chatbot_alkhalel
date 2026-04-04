using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Primitives;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Application.Models;
using MunicipalityChatbot.Domain.Entities;
using MunicipalityChatbot.Infrastructure.Config;

namespace MunicipalityChatbot.Api;

public sealed class ChatOrchestrator(
    WidgetOptions widgetOptions,
    CorsOptions corsOptions,
    QdrantOptions qdrantOptions,
    IQdrantService qdrant,
    IEmbeddingService embeddings,
    IPlanningService planner,
    IRagAnswerService rag,
    IGeneralAnswerService general,
    IApiAnswerService apiAnswer,
    IApiExecutionService apiExec,
    IFaqRepository faqs,
    IDocumentRepository docs,
    IApiDefinitionRepository apis,
    IChatAuditRepository audit,
    ILogger<ChatOrchestrator> logger
)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PublicChatResponse> HandlePublicAsync(PublicChatRequest req, string origin, string widgetApiKey, string? userToken, string? customerId, CancellationToken ct)
    {
        // Detect language from message content - this takes priority over UI language for retrieval
        var detectedLang = DetectLang(req.Message);
        // Use detected language for retrieval and responses to match the user's actual message
        var userLang = detectedLang;
        var sessionId = req.SessionId ?? Guid.Empty;

        // Optional widget guardrails
        // Note: origin can be "null" (string) when opened from file:// URL - treat as no origin
        var isNullOrigin = string.IsNullOrWhiteSpace(origin) || origin == "null";
        if (!isNullOrigin)
        {
            var widgetAllowed = (corsOptions.WidgetAllowedOrigins ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var isWidgetAllowed = widgetAllowed.Length == 0
                || widgetAllowed.Contains("*")
                || widgetAllowed.Contains(origin, StringComparer.OrdinalIgnoreCase);
            if (!isWidgetAllowed)
                throw new InvalidOperationException("Widget origin is not allowlisted.");

            if (!string.IsNullOrWhiteSpace(widgetOptions.ApiKey) &&
                !string.Equals(widgetOptions.ApiKey, widgetApiKey, StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid widget API key.");
        }

        if (sessionId == Guid.Empty)
        {
            sessionId = await audit.CreateSessionAsync(new ChatSession
            {
                Channel = isNullOrigin ? "web" : "widget",
                WidgetOrigin = isNullOrigin ? null : origin,
                UserLang = userLang,
            }, ct);
        }

        // Fetch conversation history for context (before adding current message)
        var existingMessages = await audit.GetRecentMessagesAsync(sessionId, 10, ct);
        var conversationHistory = existingMessages
            .Select(m => new ConversationMessage(m.Role, m.Text))
            .ToList();

        // Fix language detection for non-text messages (phone numbers, "?", "ok", "no cancel", etc.)
        // If the current message has no Arabic chars but conversation history does, keep Arabic
        if (userLang != "ar" && conversationHistory.Count > 0)
        {
            var isContextDependent = IsNonTextMessage(req.Message) || IsRejectionMessage(req.Message) || ConfirmationWords.Contains(req.Message.Trim());
            if (isContextDependent)
            {
                var hasArabicHistory = conversationHistory.Any(m => m.Role == "user" && m.Text.Any(c => c is >= '\u0600' and <= '\u06FF'));
                if (hasArabicHistory)
                {
                    userLang = "ar";
                    logger.LogInformation("Language override: {Detected} → ar (context-dependent message in Arabic conversation)", detectedLang);
                }
            }
        }

        var userMsg = new ChatMessage
        {
            SessionId = sessionId,
            Role = "user",
            Text = req.Message
        };
        await audit.AddMessageAsync(userMsg, ct);

        PlannerResult plan;
        List<object> faqCandidates;
        List<object> docCandidates;
        List<object> apiDefsForPlanner;

        try
        {
            // 1) Retrieval FIRST
            await qdrant.EnsureCollectionAsync(qdrantOptions.VectorSize, ct);
            var queryVec = await embeddings.EmbedAsync(req.Message, ct);

            var qdrantLang = userLang == "ar" ? "AR" : "EN";
            var faqHits = await qdrant.SearchAsync("faq", queryVec, topK: 5, language: qdrantLang, ct);
            var docHits = await qdrant.SearchAsync("doc_chunk", queryVec, topK: 8, language: null, ct);
            var websiteHits = await qdrant.SearchAsync("website", queryVec, topK: 5, language: null, ct);

            // 2) Planner (strict JSON)
            var apiDefs = await apis.ListAllowedInChatAsync(ct);
            faqCandidates = faqHits.Select(h => new
            {
                faqId = GetString(h.Payload, "faqId") ?? h.PointId,
                title = GetString(h.Payload, "title"),
                question = GetString(h.Payload, "question"),
                shortDescription = GetString(h.Payload, "shortDescription"),
                language = GetString(h.Payload, "language"),
                tags = GetString(h.Payload, "tags"),
                department = GetString(h.Payload, "department"),
                score = h.Score
            }).Cast<object>().ToList();

            // Combine document chunks and website chunks
            var docChunkCandidates = docHits.Select(h => new
            {
                chunkId = GetString(h.Payload, "chunkId") ?? h.PointId,
                docId = GetString(h.Payload, "docId"),
                filename = GetString(h.Payload, "filename"),
                filetype = GetString(h.Payload, "filetype"),
                language = GetString(h.Payload, "language"),
                page = GetInt(h.Payload, "page"),
                sheet = GetString(h.Payload, "sheet"),
                chunkIndex = GetInt(h.Payload, "chunkIndex"),
                text = GetString(h.Payload, "text"),
                source = "document",
                score = h.Score
            }).Cast<object>();

            var websiteChunkCandidates = websiteHits.Select(h => new
            {
                chunkId = GetString(h.Payload, "chunkId") ?? h.PointId,
                docId = GetString(h.Payload, "pageId"),
                filename = GetString(h.Payload, "url"),
                filetype = "website",
                language = (string?)null,
                page = (int?)null,
                sheet = GetString(h.Payload, "title"),
                chunkIndex = GetInt(h.Payload, "chunkIndex"),
                text = GetString(h.Payload, "text"),
                source = "website",
                score = h.Score
            }).Cast<object>();

            docCandidates = docChunkCandidates.Concat(websiteChunkCandidates)
                .OrderByDescending(c => ((dynamic)c).score)
                .Take(10)
                .ToList();

            apiDefsForPlanner = apiDefs.Select(a => new
            {
                apiId = a.ApiId,
                apiName = a.ApiName,
                description = a.Description,
                baseUrl = a.BaseUrl,
                method = a.Method,
                pathTemplate = a.PathTemplate,
                queryParamsSchema = a.QueryParamsSchemaJson,
                bodySchema = a.BodySchemaJson,
                headersTemplate = a.HeadersTemplateJson
            }).Cast<object>().ToList();

            var plannerInput = new PlannerInput
            {
                UserMessage = req.Message,
                UserLang = userLang,
                FaqCandidates = faqCandidates,
                DocChunkCandidates = docCandidates,
                ApiDefinitions = apiDefsForPlanner,
                SessionState = new { },
                ConversationHistory = conversationHistory.Count > 0 ? conversationHistory : null
            };

            plan = await planner.PlanAsync(plannerInput, ct);
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogWarning(ex, "AI provider request failed (StatusCode={StatusCode}). Returning friendly fallback.", ex.StatusCode);
            var msg = userLang == "ar"
                ? "الخدمة مشغولة حالياً. الرجاء المحاولة بعد قليل."
                : "The AI service is busy right now. Please try again in a moment.";
            return new PublicChatResponse(sessionId, "GENERAL", msg, [], null);
        }

        var decision = new RoutingDecision
        {
            SessionId = sessionId,
            MessageId = userMsg.MessageId,
            Route = NormalizeRoute(plan.Route),
            Confidence = plan.Confidence,
            SelectedFaqId = Guid.TryParse(plan.SelectedFaqId, out var fid) ? fid : null,
            SelectedChunkIdsCsv = plan.SelectedChunkIds is { Count: > 0 } ? string.Join(",", plan.SelectedChunkIds) : null,
            SelectedApiId = plan.ApiCall?.ApiId is { } aid && Guid.TryParse(aid, out var apiId) ? apiId : null,
            PlannerJson = plan.RawJson
        };

        // Safety net 0: Rejection detection — if last assistant message was confirmation card
        // and user rejected, cancel the complaint flow entirely
        // BUT: corrections (user wants to fix a field) bypass rejection
        if (conversationHistory.Count > 0 && IsConfirmationContext(conversationHistory)
            && !IsCorrectionMessage(req.Message) && IsRejectionMessage(req.Message))
        {
            var prevRoute = decision.Route;
            decision.Route = "GENERAL";
            plan.Route = "GENERAL";
            plan.ApiCall = null;
            plan.RequiresConfirmation = false;
            plan.UserConfirmed = false;
            plan.FollowUpQuestion = null;
            plan.PendingSubmissionSummary = null;
            logger.LogInformation("Server-side rejection detected: {Prev} → GENERAL (cancelled complaint) for: {Message}", prevRoute, req.Message);
        }

        // Safety net 0.5: Greeting detection — greetings should ALWAYS route to GENERAL
        // regardless of conversation history or planner decision
        if (decision.Route != "GENERAL" && IsGreeting(req.Message))
        {
            var prevRoute = decision.Route;
            decision.Route = "GENERAL";
            plan.Route = "GENERAL";
            plan.ApiCall = null;
            plan.SelectedFaqId = null;
            plan.SelectedChunkIds = null;
            plan.FollowUpQuestion = null;
            logger.LogInformation("Greeting safety net: {Prev} → GENERAL for: {Message}", prevRoute, req.Message);
        }

        // Safety net 0.6: Protest detection — "شو دخل X" / "لم اسأل عن X" = user protesting wrong response
        if (decision.Route != "GENERAL" && IsProtestMessage(req.Message))
        {
            var prevRoute = decision.Route;
            decision.Route = "GENERAL";
            plan.Route = "GENERAL";
            plan.ApiCall = null;
            plan.SelectedFaqId = null;
            plan.SelectedChunkIds = null;
            plan.FollowUpQuestion = null;
            logger.LogInformation("Protest safety net: {Prev} → GENERAL for: {Message}", prevRoute, req.Message);
        }

        // Safety net 1: API keyword detection — catches misroutes for known GET APIs
        var apiKeywordPath = MatchGetApiKeywords(req.Message);
        if (apiKeywordPath is not null && decision.Route != "GENERAL") // skip if already cancelled by rejection
        {
            var matchedApi = apiDefsForPlanner.Cast<dynamic>()
                .FirstOrDefault(a => ((string)a.pathTemplate).Contains(apiKeywordPath));
            if (matchedApi is not null)
            {
                var matchedApiId = Guid.Parse(matchedApi.apiId.ToString());
                if (decision.SelectedApiId != matchedApiId) // only override if planner picked wrong API
                {
                    var prevRoute = decision.Route;
                    decision.Route = "API";
                    decision.SelectedApiId = matchedApiId;
                    plan.Route = "API";
                    plan.ApiCall = new PlannerApiCall { ApiId = matchedApi.apiId.ToString(), Params = new() };
                    plan.FollowUpQuestion = null;
                    plan.RequiresConfirmation = false;
                    plan.UserConfirmed = false;
                    logger.LogInformation("API keyword safety net: {Prev} → API ({Path})", prevRoute, apiKeywordPath);
                }
            }
        }

        // Safety net 1.5: Complaint initiation detection — if user clearly wants a complaint
        // but planner routed to a GET API (e.g. water schedule due to conversation history), override
        if (IsComplaintInitiation(req.Message) && !IsInComplaintFlow(conversationHistory))
        {
            var complaintApi = apiDefsForPlanner.Cast<dynamic>()
                .FirstOrDefault(a => ((string)a.method).Equals("POST", StringComparison.OrdinalIgnoreCase));
            if (complaintApi is not null)
            {
                var complaintApiId = Guid.Parse(complaintApi.apiId.ToString());
                if (decision.SelectedApiId != complaintApiId)
                {
                    var prevRoute = decision.Route;
                    decision.Route = "API";
                    decision.SelectedApiId = complaintApiId;
                    plan.Route = "API";
                    plan.ApiCall = new PlannerApiCall { ApiId = complaintApi.apiId.ToString(), Params = new() };
                    plan.RequiresConfirmation = false;
                    plan.UserConfirmed = false;
                    plan.PendingSubmissionSummary = null;
                    plan.FollowUpQuestion = GenerateComplaintFollowUp(req.Message, userLang);
                    logger.LogInformation("Complaint keyword safety net: {Prev} → complaint API for: {Message}", prevRoute, req.Message);
                }
            }
        }

        // Safety net 2: override GENERAL when strong FAQ/RAG matches exist
        // BUT only for FAQ if the message looks like a question/request (not greetings or personal statements)
        if (decision.Route == "GENERAL" && !(conversationHistory.Count > 0 && IsConfirmationContext(conversationHistory) && IsRejectionMessage(req.Message)))
        {
            var topFaq = faqCandidates.Count > 0 ? (dynamic)faqCandidates[0] : null;
            if (topFaq is not null && (float)topFaq.score > 0.72f
                && Guid.TryParse((string)topFaq.faqId, out var overrideFaqId)
                && IsQuestionOrRequest(req.Message)
                && !IsGreeting(req.Message))
            {
                decision.Route = "FAQ";
                decision.SelectedFaqId = overrideFaqId;
                logger.LogInformation("Routing safety net: GENERAL → FAQ (top FAQ score={Score:F2})", (float)topFaq.score);
            }
            else if (docCandidates.Count > 0 && !IsGreeting(req.Message))
            {
                var topChunk = (dynamic)docCandidates[0];
                if ((float)topChunk.score > 0.35f)
                {
                    var overrideChunkIds = docCandidates.Cast<dynamic>()
                        .Where(c => (float)c.score > 0.3f)
                        .Take(6)
                        .Select(c => (string)c.chunkId)
                        .ToList();
                    if (overrideChunkIds.Count > 0)
                    {
                        decision.Route = "RAG";
                        decision.SelectedChunkIdsCsv = string.Join(",", overrideChunkIds);
                        plan.SelectedChunkIds = overrideChunkIds;
                        logger.LogInformation("Routing safety net: GENERAL → RAG (top chunk score={Score:F2})", (float)topChunk.score);
                    }
                }
            }
        }

        await audit.AddRoutingDecisionAsync(decision, ct);

        // 3) Enforce routing rules + produce answer
        var route = decision.Route;

        if (route == "FAQ" && decision.SelectedFaqId is { } selectedFaqId)
        {
            var faq = await faqs.GetByIdAsync(selectedFaqId, ct);
            if (faq is null) route = "GENERAL";
            else
            {
                // Save assistant response
                await audit.AddMessageAsync(new ChatMessage
                {
                    SessionId = sessionId,
                    Role = "assistant",
                    Text = faq.Answer
                }, ct);

                return new PublicChatResponse(
                    SessionId: sessionId,
                    Route: "FAQ",
                    Answer: faq.Answer, // exact answer; no rewriting
                    Citations: [],
                    FollowUpQuestion: null
                );
            }
        }

        if (route == "RAG" && plan.SelectedChunkIds is { Count: > 0 })
        {
            var chunkIds = plan.SelectedChunkIds
                .Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToArray();

            var dbChunks = await docs.GetChunksByIdsAsync(chunkIds, ct);
            var chunks = new List<DocumentChunk>(dbChunks);

            // Fallback: if some/all chunks not found in PostgreSQL (e.g. website chunks),
            // create synthetic chunks from the Qdrant payload text we already have
            if (chunks.Count < chunkIds.Length)
            {
                var foundIds = chunks.Select(c => c.ChunkId).ToHashSet();
                foreach (var candidate in docCandidates)
                {
                    var dyn = (dynamic)candidate;
                    var candidateId = (string)dyn.chunkId;
                    if (Guid.TryParse(candidateId, out var cid) && chunkIds.Contains(cid) && !foundIds.Contains(cid))
                    {
                        var text = (string?)dyn.text;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            chunks.Add(new DocumentChunk
                            {
                                ChunkId = cid,
                                DocId = Guid.Empty,
                                Filename = (string?)dyn.filename ?? "website",
                                FileType = (string?)dyn.filetype ?? "website",
                                Text = text,
                                ChunkIndex = (int?)dyn.chunkIndex ?? 0
                            });
                            foundIds.Add(cid);
                        }
                    }
                }
                logger.LogInformation("Chunk fallback: {Found} from DB, {Total} total after Qdrant payload fallback", dbChunks.Count, chunks.Count);
            }

            if (chunks.Count == 0) route = "GENERAL";
            else
            {
                var answer = await rag.AnswerFromChunksAsync(req.Message, userLang, chunks, ct);
                // Dedupe citations by filename (show each source file only once)
                var citations = chunks
                    .GroupBy(c => c.Filename)
                    .Select(g => new Citation(g.Key, string.Join(",", g.Select(c => c.ChunkId))))
                    .ToList();

                // Save assistant response
                await audit.AddMessageAsync(new ChatMessage
                {
                    SessionId = sessionId,
                    Role = "assistant",
                    Text = answer
                }, ct);

                return new PublicChatResponse(sessionId, "RAG", answer, citations, null);
            }
        }

        if (route == "API")
        {
            if (!string.IsNullOrWhiteSpace(plan.FollowUpQuestion))
            {
                // Save follow-up as assistant message so it appears in conversation history
                await audit.AddMessageAsync(new ChatMessage
                {
                    SessionId = sessionId,
                    Role = "assistant",
                    Text = plan.FollowUpQuestion
                }, ct);

                return new PublicChatResponse(sessionId, "API", "", [], plan.FollowUpQuestion);
            }

            var apiIdStr = plan.ApiCall?.ApiId;
            if (!Guid.TryParse(apiIdStr, out var parsedApiId))
                route = "GENERAL";
            else
            {
                var api = await apis.GetByIdAsync(parsedApiId, ct);
                if (api is null || !api.AllowInChat) route = "GENERAL";
                else
                {
                    // POST API confirmation flow - SERVER-SIDE ENFORCEMENT
                    // For POST APIs, NEVER execute without explicit user confirmation
                    if (api.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                    {
                        var lastAssistant = conversationHistory.LastOrDefault(m => m.Role == "assistant");
                        var confirmationCardShown = lastAssistant is not null &&
                            (lastAssistant.Text.Contains("هل تريد تأكيد") || lastAssistant.Text.Contains("Do you want to confirm"));

                        // If planner set userConfirmed=true but no confirmation card was shown, override it
                        if (plan.UserConfirmed && !confirmationCardShown)
                        {
                            plan.UserConfirmed = false;
                            logger.LogInformation("POST confirmation enforcement: blocked direct submission without confirmation card for: {Message}", req.Message);
                        }

                        // Server-side fallback: detect confirmation even if planner missed it
                        if (!plan.UserConfirmed && confirmationCardShown && IsConfirmationMessage(req.Message))
                        {
                            plan.UserConfirmed = true;
                            logger.LogInformation("Server-side confirmation detected for message: {Message}", req.Message);
                        }

                        // If still not confirmed, show confirmation card or block
                        if (!plan.UserConfirmed)
                        {
                            var bodyParams = plan.ApiCall?.Params?.Body;
                            if (bodyParams is not null && bodyParams.Count > 0)
                            {
                                // Server-side correction enforcement: if user is correcting a field,
                                // apply the correction to body params (planner often copies old values)
                                if (IsCorrectionMessage(req.Message))
                                {
                                    ApplyCorrectionToBodyParams(req.Message, bodyParams, conversationHistory);
                                    logger.LogInformation("Applied server-side correction to body params for: {Message}", req.Message);
                                }

                                // Always regenerate summary from body params to ensure it reflects corrections
                                var summary = GenerateComplaintSummary(bodyParams, userLang);

                                var confirmPrompt = userLang == "ar"
                                    ? $"{summary}\n\nهل تريد تأكيد إرسال هذه الشكوى؟ (نعم/لا)"
                                    : $"{summary}\n\nDo you want to confirm submitting this complaint? (yes/no)";

                                await audit.AddMessageAsync(new ChatMessage
                                {
                                    SessionId = sessionId,
                                    Role = "assistant",
                                    Text = confirmPrompt
                                }, ct);

                                return new PublicChatResponse(sessionId, "API", confirmPrompt, [], null);
                            }
                            // No body params → shouldn't reach here (followUpQuestion handled earlier), but block execution
                            logger.LogWarning("POST API reached execution without body params or confirmation for: {Message}", req.Message);
                        }
                    }

                    // Check if API requires user token but none provided
                    if (api.AuthType.Equals("UserToken", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(userToken))
                    {
                        var authMsg = userLang == "ar"
                            ? "هذه الخدمة تتطلب تسجيل الدخول. يرجى تسجيل الدخول في التطبيق أولاً ثم المحاولة مرة أخرى."
                            : "This service requires authentication. Please log in to the app first and try again.";

                        await audit.AddMessageAsync(new ChatMessage
                        {
                            SessionId = sessionId,
                            Role = "assistant",
                            Text = authMsg
                        }, ct);

                        return new PublicChatResponse(sessionId, "API", authMsg, [], null);
                    }

                    // Execute the API (either GET, or POST with userConfirmed=true)
                    var exec = await apiExec.ExecuteAsync(api, plan.ApiCall!, ct, userToken);

                    await audit.AddApiCallAsync(new ApiCallAudit
                    {
                        SessionId = sessionId,
                        MessageId = userMsg.MessageId,
                        ApiId = api.ApiId,
                        RequestSummaryJson = JsonSerializer.Serialize(new
                        {
                            apiId = api.ApiId,
                            apiName = api.ApiName,
                            baseUrl = api.BaseUrl,
                            method = api.Method,
                            pathTemplate = api.PathTemplate,
                            plannerParams = plan.ApiCall?.Params
                        }, JsonOptions),
                        ResponseStatusCode = exec.StatusCode,
                        ResponseSummaryJson = JsonSerializer.Serialize(new
                        {
                            success = exec.Success,
                            statusCode = exec.StatusCode
                        }, JsonOptions),
                        Error = exec.Error
                    }, ct);

                    // Handle API execution failure gracefully
                    string answer;
                    if (!exec.Success || string.IsNullOrWhiteSpace(exec.ResponseBody))
                    {
                        answer = userLang == "ar"
                            ? "عذرًا، لم نتمكن من الحصول على البيانات المطلوبة حاليًا. يرجى المحاولة مرة أخرى لاحقًا."
                            : "Sorry, we couldn't retrieve the requested data at this time. Please try again later.";
                    }
                    else
                    {
                        answer = await apiAnswer.AnswerFromApiResultAsync(req.Message, userLang, api.ApiName, exec.ResponseBody, api.ResponseHandlingNotes, ct);
                    }

                    // Save assistant response
                    await audit.AddMessageAsync(new ChatMessage
                    {
                        SessionId = sessionId,
                        Role = "assistant",
                        Text = answer
                    }, ct);

                    return new PublicChatResponse(sessionId, "API", answer, [], null);
                }
            }
        }

        // GENERAL fallback
        var generalAnswer = await general.AnswerGeneralAsync(req.Message, userLang, ct);

        // Save assistant response
        await audit.AddMessageAsync(new ChatMessage
        {
            SessionId = sessionId,
            Role = "assistant",
            Text = generalAnswer
        }, ct);

        return new PublicChatResponse(sessionId, "GENERAL", generalAnswer, [], null);
    }

    public async IAsyncEnumerable<StreamEvent> HandlePublicStreamAsync(
        PublicChatRequest req, string origin, string widgetApiKey, string? userToken, string? customerId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Detect language from message content - this takes priority over UI language for retrieval
        var detectedLang = DetectLang(req.Message);
        // Use detected language for retrieval and responses to match the user's actual message
        var userLang = detectedLang;
        var sessionId = req.SessionId ?? Guid.Empty;

        // Widget validation (same as non-streaming)
        // Note: origin can be "null" (string) when opened from file:// URL - treat as no origin
        var isNullOrigin = string.IsNullOrWhiteSpace(origin) || origin == "null";
        if (!isNullOrigin)
        {
            var widgetAllowed = (corsOptions.WidgetAllowedOrigins ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var isWidgetAllowed = widgetAllowed.Length == 0
                || widgetAllowed.Contains("*")
                || widgetAllowed.Contains(origin, StringComparer.OrdinalIgnoreCase);
            if (!isWidgetAllowed)
            {
                yield return new StreamEvent("error", Error: "Widget origin is not allowlisted.");
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(widgetOptions.ApiKey) &&
                !string.Equals(widgetOptions.ApiKey, widgetApiKey, StringComparison.Ordinal))
            {
                yield return new StreamEvent("error", Error: "Invalid widget API key.");
                yield break;
            }
        }

        if (sessionId == Guid.Empty)
        {
            sessionId = await audit.CreateSessionAsync(new ChatSession
            {
                Channel = isNullOrigin ? "web" : "widget",
                WidgetOrigin = isNullOrigin ? null : origin,
                UserLang = userLang,
            }, ct);
        }

        // Fetch conversation history for context (before adding current message)
        var existingMessages = await audit.GetRecentMessagesAsync(sessionId, 10, ct);
        var conversationHistory = existingMessages
            .Select(m => new ConversationMessage(m.Role, m.Text))
            .ToList();

        // Fix language detection for non-text messages (phone numbers, "?", "ok", "no cancel", etc.)
        if (userLang != "ar" && conversationHistory.Count > 0)
        {
            var isContextDependent = IsNonTextMessage(req.Message) || IsRejectionMessage(req.Message) || ConfirmationWords.Contains(req.Message.Trim());
            if (isContextDependent)
            {
                var hasArabicHistory = conversationHistory.Any(m => m.Role == "user" && m.Text.Any(c => c is >= '\u0600' and <= '\u06FF'));
                if (hasArabicHistory)
                {
                    userLang = "ar";
                    logger.LogInformation("Language override (stream): {Detected} → ar (context-dependent message in Arabic conversation)", detectedLang);
                }
            }
        }

        var userMsg = new ChatMessage
        {
            SessionId = sessionId,
            Role = "user",
            Text = req.Message
        };
        await audit.AddMessageAsync(userMsg, ct);

        PlannerResult? plan = null;
        List<object> faqCandidates = [];
        List<object> docCandidates = [];
        string? plannerError = null;

        // Stage 1: Searching documents
        yield return new StreamEvent("stage", Stage: userLang == "ar" ? "جارٍ البحث في المستندات..." : "Searching documents...");

        // 1) Retrieval
        await qdrant.EnsureCollectionAsync(qdrantOptions.VectorSize, ct);
        var queryVec = await embeddings.EmbedAsync(req.Message, ct);

        var qdrantLang = userLang == "ar" ? "AR" : "EN";
        var faqHits = await qdrant.SearchAsync("faq", queryVec, topK: 5, language: qdrantLang, ct);
        var docHits = await qdrant.SearchAsync("doc_chunk", queryVec, topK: 8, language: null, ct);
        var websiteHits = await qdrant.SearchAsync("website", queryVec, topK: 5, language: null, ct);

        // Stage 2: Analyzing question
        yield return new StreamEvent("stage", Stage: userLang == "ar" ? "جارٍ تحليل السؤال..." : "Analyzing question...");

        // 2) Planner
        var apiDefs = await apis.ListAllowedInChatAsync(ct);
        faqCandidates = faqHits.Select(h => new
        {
            faqId = GetString(h.Payload, "faqId") ?? h.PointId,
            title = GetString(h.Payload, "title"),
            question = GetString(h.Payload, "question"),
            shortDescription = GetString(h.Payload, "shortDescription"),
            language = GetString(h.Payload, "language"),
            tags = GetString(h.Payload, "tags"),
            department = GetString(h.Payload, "department"),
            score = h.Score
        }).Cast<object>().ToList();

        // Combine document chunks and website chunks
        var docChunkCandidates = docHits.Select(h => new
        {
            chunkId = GetString(h.Payload, "chunkId") ?? h.PointId,
            docId = GetString(h.Payload, "docId"),
            filename = GetString(h.Payload, "filename"),
            filetype = GetString(h.Payload, "filetype"),
            language = GetString(h.Payload, "language"),
            page = GetInt(h.Payload, "page"),
            sheet = GetString(h.Payload, "sheet"),
            chunkIndex = GetInt(h.Payload, "chunkIndex"),
            text = GetString(h.Payload, "text"),
            source = "document",
            score = h.Score
        }).Cast<object>();

        var websiteChunkCandidates = websiteHits.Select(h => new
        {
            chunkId = GetString(h.Payload, "chunkId") ?? h.PointId,
            docId = GetString(h.Payload, "pageId"),
            filename = GetString(h.Payload, "url"),
            filetype = "website",
            language = (string?)null,
            page = (int?)null,
            sheet = GetString(h.Payload, "title"),
            chunkIndex = GetInt(h.Payload, "chunkIndex"),
            text = GetString(h.Payload, "text"),
            source = "website",
            score = h.Score
        }).Cast<object>();

        docCandidates = docChunkCandidates.Concat(websiteChunkCandidates)
            .OrderByDescending(c => ((dynamic)c).score)
            .Take(10)
            .ToList();

        var apiDefsForPlanner = apiDefs.Select(a => new
        {
            apiId = a.ApiId,
            apiName = a.ApiName,
            description = a.Description,
            baseUrl = a.BaseUrl,
            method = a.Method,
            pathTemplate = a.PathTemplate,
            queryParamsSchema = a.QueryParamsSchemaJson,
            bodySchema = a.BodySchemaJson,
            headersTemplate = a.HeadersTemplateJson
        }).Cast<object>().ToList();

        var plannerInput = new PlannerInput
        {
            UserMessage = req.Message,
            UserLang = userLang,
            FaqCandidates = faqCandidates,
            DocChunkCandidates = docCandidates,
            ApiDefinitions = apiDefsForPlanner,
            SessionState = new { },
            ConversationHistory = conversationHistory.Count > 0 ? conversationHistory : null
        };

        try
        {
            plan = await planner.PlanAsync(plannerInput, ct);
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            plannerError = userLang == "ar"
                ? "الخدمة مشغولة حالياً. الرجاء المحاولة بعد قليل."
                : "The AI service is busy right now. Please try again in a moment.";
        }

        // Handle planner error outside catch block
        if (plannerError is not null)
        {
            yield return new StreamEvent("meta", sessionId, "GENERAL", [], null);
            yield return new StreamEvent("chunk", Content: plannerError);
            yield break;
        }

        var decision = new RoutingDecision
        {
            SessionId = sessionId,
            MessageId = userMsg.MessageId,
            Route = NormalizeRoute(plan.Route),
            Confidence = plan.Confidence,
            SelectedFaqId = Guid.TryParse(plan.SelectedFaqId, out var fid) ? fid : null,
            SelectedChunkIdsCsv = plan.SelectedChunkIds is { Count: > 0 } ? string.Join(",", plan.SelectedChunkIds) : null,
            SelectedApiId = plan.ApiCall?.ApiId is { } aid && Guid.TryParse(aid, out var apiId) ? apiId : null,
            PlannerJson = plan.RawJson
        };

        // Safety net 0: Rejection detection — if last assistant message was confirmation card
        // and user rejected, cancel the complaint flow entirely
        // BUT: corrections (user wants to fix a field) bypass rejection
        if (conversationHistory.Count > 0 && IsConfirmationContext(conversationHistory)
            && !IsCorrectionMessage(req.Message) && IsRejectionMessage(req.Message))
        {
            var prevRoute = decision.Route;
            decision.Route = "GENERAL";
            plan.Route = "GENERAL";
            plan.ApiCall = null;
            plan.RequiresConfirmation = false;
            plan.UserConfirmed = false;
            plan.FollowUpQuestion = null;
            plan.PendingSubmissionSummary = null;
            logger.LogInformation("Server-side rejection detected (stream): {Prev} → GENERAL (cancelled complaint) for: {Message}", prevRoute, req.Message);
        }

        // Safety net 0.5: Greeting detection — greetings should ALWAYS route to GENERAL (streaming)
        if (decision.Route != "GENERAL" && IsGreeting(req.Message))
        {
            var prevRoute = decision.Route;
            decision.Route = "GENERAL";
            plan.Route = "GENERAL";
            plan.ApiCall = null;
            plan.SelectedFaqId = null;
            plan.SelectedChunkIds = null;
            plan.FollowUpQuestion = null;
            logger.LogInformation("Greeting safety net (stream): {Prev} → GENERAL for: {Message}", prevRoute, req.Message);
        }

        // Safety net 0.6: Protest detection (streaming)
        if (decision.Route != "GENERAL" && IsProtestMessage(req.Message))
        {
            var prevRoute = decision.Route;
            decision.Route = "GENERAL";
            plan.Route = "GENERAL";
            plan.ApiCall = null;
            plan.SelectedFaqId = null;
            plan.SelectedChunkIds = null;
            plan.FollowUpQuestion = null;
            logger.LogInformation("Protest safety net (stream): {Prev} → GENERAL for: {Message}", prevRoute, req.Message);
        }

        // Safety net 1: API keyword detection — catches misroutes for known GET APIs
        var apiKeywordPath = MatchGetApiKeywords(req.Message);
        if (apiKeywordPath is not null && decision.Route != "GENERAL") // skip if already cancelled by rejection
        {
            var matchedApi = apiDefsForPlanner.Cast<dynamic>()
                .FirstOrDefault(a => ((string)a.pathTemplate).Contains(apiKeywordPath));
            if (matchedApi is not null)
            {
                var matchedApiId = Guid.Parse(matchedApi.apiId.ToString());
                if (decision.SelectedApiId != matchedApiId)
                {
                    var prevRoute = decision.Route;
                    decision.Route = "API";
                    decision.SelectedApiId = matchedApiId;
                    plan.Route = "API";
                    plan.ApiCall = new PlannerApiCall { ApiId = matchedApi.apiId.ToString(), Params = new() };
                    plan.FollowUpQuestion = null;
                    plan.RequiresConfirmation = false;
                    plan.UserConfirmed = false;
                    logger.LogInformation("API keyword safety net: {Prev} → API ({Path})", prevRoute, apiKeywordPath);
                }
            }
        }

        // Safety net 1.5: Complaint initiation detection (streaming path)
        if (IsComplaintInitiation(req.Message) && !IsInComplaintFlow(conversationHistory))
        {
            var complaintApi = apiDefsForPlanner.Cast<dynamic>()
                .FirstOrDefault(a => ((string)a.method).Equals("POST", StringComparison.OrdinalIgnoreCase));
            if (complaintApi is not null)
            {
                var complaintApiId = Guid.Parse(complaintApi.apiId.ToString());
                if (decision.SelectedApiId != complaintApiId)
                {
                    var prevRoute = decision.Route;
                    decision.Route = "API";
                    decision.SelectedApiId = complaintApiId;
                    plan.Route = "API";
                    plan.ApiCall = new PlannerApiCall { ApiId = complaintApi.apiId.ToString(), Params = new() };
                    plan.RequiresConfirmation = false;
                    plan.UserConfirmed = false;
                    plan.PendingSubmissionSummary = null;
                    plan.FollowUpQuestion = GenerateComplaintFollowUp(req.Message, userLang);
                    logger.LogInformation("Complaint keyword safety net (stream): {Prev} → complaint API for: {Message}", prevRoute, req.Message);
                }
            }
        }

        // Safety net 2: override GENERAL when strong FAQ/RAG matches exist
        // BUT skip if we just rejected a confirmation (don't re-route the cancellation)
        // AND only override to FAQ if the message looks like a question/request (not greetings/personal statements)
        if (decision.Route == "GENERAL" && !(conversationHistory.Count > 0 && IsConfirmationContext(conversationHistory) && IsRejectionMessage(req.Message)))
        {
            var topFaq = faqCandidates.Count > 0 ? (dynamic)faqCandidates[0] : null;
            if (topFaq is not null && (float)topFaq.score > 0.72f
                && Guid.TryParse((string)topFaq.faqId, out var overrideFaqId)
                && IsQuestionOrRequest(req.Message)
                && !IsGreeting(req.Message))
            {
                decision.Route = "FAQ";
                decision.SelectedFaqId = overrideFaqId;
                logger.LogInformation("Routing safety net: GENERAL → FAQ (top FAQ score={Score:F2})", (float)topFaq.score);
            }
            else if (docCandidates.Count > 0 && !IsGreeting(req.Message))
            {
                var topChunk = (dynamic)docCandidates[0];
                if ((float)topChunk.score > 0.35f)
                {
                    var overrideChunkIds = docCandidates.Cast<dynamic>()
                        .Where(c => (float)c.score > 0.3f)
                        .Take(6)
                        .Select(c => (string)c.chunkId)
                        .ToList();
                    if (overrideChunkIds.Count > 0)
                    {
                        decision.Route = "RAG";
                        decision.SelectedChunkIdsCsv = string.Join(",", overrideChunkIds);
                        plan.SelectedChunkIds = overrideChunkIds;
                        logger.LogInformation("Routing safety net: GENERAL → RAG (top chunk score={Score:F2})", (float)topChunk.score);
                    }
                }
            }
        }

        await audit.AddRoutingDecisionAsync(decision, ct);

        var route = decision.Route;

        // FAQ route - not streamed (instant answer from DB)
        if (route == "FAQ" && decision.SelectedFaqId is { } selectedFaqId)
        {
            var faq = await faqs.GetByIdAsync(selectedFaqId, ct);
            if (faq is not null)
            {
                // Save assistant response
                await audit.AddMessageAsync(new ChatMessage
                {
                    SessionId = sessionId,
                    Role = "assistant",
                    Text = faq.Answer
                }, ct);

                yield return new StreamEvent("stage", Stage: userLang == "ar" ? "تم العثور على إجابة..." : "Found answer...");
                yield return new StreamEvent("meta", sessionId, "FAQ", [], null);
                yield return new StreamEvent("chunk", Content: faq.Answer);
                yield break;
            }
            route = "GENERAL";
        }

        // RAG route - streamed
        if (route == "RAG" && plan.SelectedChunkIds is { Count: > 0 })
        {
            var chunkIds = plan.SelectedChunkIds
                .Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToArray();

            var dbChunks = await docs.GetChunksByIdsAsync(chunkIds, ct);
            var chunks = new List<DocumentChunk>(dbChunks);

            // Fallback: if some/all chunks not found in PostgreSQL (e.g. website chunks),
            // create synthetic chunks from the Qdrant payload text we already have
            if (chunks.Count < chunkIds.Length)
            {
                var foundIds = chunks.Select(c => c.ChunkId).ToHashSet();
                foreach (var candidate in docCandidates)
                {
                    var dyn = (dynamic)candidate;
                    var candidateId = (string)dyn.chunkId;
                    if (Guid.TryParse(candidateId, out var cid) && chunkIds.Contains(cid) && !foundIds.Contains(cid))
                    {
                        var text = (string?)dyn.text;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            chunks.Add(new DocumentChunk
                            {
                                ChunkId = cid,
                                DocId = Guid.Empty,
                                Filename = (string?)dyn.filename ?? "website",
                                FileType = (string?)dyn.filetype ?? "website",
                                Text = text,
                                ChunkIndex = (int?)dyn.chunkIndex ?? 0
                            });
                            foundIds.Add(cid);
                        }
                    }
                }
                logger.LogInformation("Chunk fallback (stream): {Found} from DB, {Total} total after Qdrant payload fallback", dbChunks.Count, chunks.Count);
            }

            if (chunks.Count > 0)
            {
                var citations = chunks
                    .GroupBy(c => c.Filename)
                    .Select(g => new Citation(g.Key, string.Join(",", g.Select(c => c.ChunkId))))
                    .ToList();

                yield return new StreamEvent("stage", Stage: userLang == "ar" ? "جارٍ إعداد الإجابة..." : "Preparing answer...");
                yield return new StreamEvent("meta", sessionId, "RAG", citations, null);

                var fullResponse = new System.Text.StringBuilder();
                await foreach (var chunk in rag.StreamAnswerFromChunksAsync(req.Message, userLang, chunks, ct))
                {
                    fullResponse.Append(chunk);
                    yield return new StreamEvent("chunk", Content: chunk);
                }

                // Save assistant response after streaming completes
                await audit.AddMessageAsync(new ChatMessage
                {
                    SessionId = sessionId,
                    Role = "assistant",
                    Text = fullResponse.ToString()
                }, ct);

                yield break;
            }
            route = "GENERAL";
        }

        // API route - answer streamed after API call
        if (route == "API")
        {
            if (!string.IsNullOrWhiteSpace(plan.FollowUpQuestion))
            {
                // Save follow-up as assistant message so it appears in conversation history
                await audit.AddMessageAsync(new ChatMessage
                {
                    SessionId = sessionId,
                    Role = "assistant",
                    Text = plan.FollowUpQuestion
                }, ct);

                yield return new StreamEvent("meta", sessionId, "API", [], plan.FollowUpQuestion);
                yield break;
            }

            var apiIdStr = plan.ApiCall?.ApiId;
            if (Guid.TryParse(apiIdStr, out var parsedApiId))
            {
                var api = await apis.GetByIdAsync(parsedApiId, ct);
                if (api is not null && api.AllowInChat)
                {
                    // POST API confirmation flow - SERVER-SIDE ENFORCEMENT
                    // For POST APIs, NEVER execute without explicit user confirmation
                    if (api.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                    {
                        var lastAssistant = conversationHistory.LastOrDefault(m => m.Role == "assistant");
                        var confirmationCardShown = lastAssistant is not null &&
                            (lastAssistant.Text.Contains("هل تريد تأكيد") || lastAssistant.Text.Contains("Do you want to confirm"));

                        // If planner set userConfirmed=true but no confirmation card was shown, override it
                        if (plan.UserConfirmed && !confirmationCardShown)
                        {
                            plan.UserConfirmed = false;
                            logger.LogInformation("POST confirmation enforcement (stream): blocked direct submission without confirmation card for: {Message}", req.Message);
                        }

                        // Server-side fallback: detect confirmation even if planner missed it
                        if (!plan.UserConfirmed && confirmationCardShown && IsConfirmationMessage(req.Message))
                        {
                            plan.UserConfirmed = true;
                            logger.LogInformation("Server-side confirmation detected (stream) for message: {Message}", req.Message);
                        }

                        // If still not confirmed, show confirmation card or block
                        if (!plan.UserConfirmed)
                        {
                            var bodyParams = plan.ApiCall?.Params?.Body;
                            if (bodyParams is not null && bodyParams.Count > 0)
                            {
                                // Server-side correction enforcement: if user is correcting a field,
                                // apply the correction to body params (planner often copies old values)
                                if (IsCorrectionMessage(req.Message))
                                {
                                    ApplyCorrectionToBodyParams(req.Message, bodyParams, conversationHistory);
                                    logger.LogInformation("Applied server-side correction (stream) to body params for: {Message}", req.Message);
                                }

                                // Always regenerate summary from body params to ensure it reflects corrections
                                var summary = GenerateComplaintSummary(bodyParams, userLang);

                                var confirmPrompt = userLang == "ar"
                                    ? $"{summary}\n\nهل تريد تأكيد إرسال هذه الشكوى؟ (نعم/لا)"
                                    : $"{summary}\n\nDo you want to confirm submitting this complaint? (yes/no)";

                                yield return new StreamEvent("meta", sessionId, "API", [], null);
                                yield return new StreamEvent("chunk", Content: confirmPrompt);

                                await audit.AddMessageAsync(new ChatMessage
                                {
                                    SessionId = sessionId,
                                    Role = "assistant",
                                    Text = confirmPrompt
                                }, ct);

                                yield break;
                            }
                            // No body params → shouldn't reach here (followUpQuestion handled earlier), but block execution
                            logger.LogWarning("POST API (stream) reached execution without body params or confirmation for: {Message}", req.Message);
                        }
                    }

                    // Check if API requires user token but none provided
                    if (api.AuthType.Equals("UserToken", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(userToken))
                    {
                        var authMsg = userLang == "ar"
                            ? "هذه الخدمة تتطلب تسجيل الدخول. يرجى تسجيل الدخول في التطبيق أولاً ثم المحاولة مرة أخرى."
                            : "This service requires authentication. Please log in to the app first and try again.";

                        yield return new StreamEvent("meta", sessionId, "API", [], null);
                        yield return new StreamEvent("chunk", Content: authMsg);

                        await audit.AddMessageAsync(new ChatMessage
                        {
                            SessionId = sessionId,
                            Role = "assistant",
                            Text = authMsg
                        }, ct);

                        yield break;
                    }

                    // Execute the API (either GET, or POST with userConfirmed=true)
                    var exec = await apiExec.ExecuteAsync(api, plan.ApiCall!, ct, userToken);

                    await audit.AddApiCallAsync(new ApiCallAudit
                    {
                        SessionId = sessionId,
                        MessageId = userMsg.MessageId,
                        ApiId = api.ApiId,
                        RequestSummaryJson = JsonSerializer.Serialize(new
                        {
                            apiId = api.ApiId,
                            apiName = api.ApiName,
                            baseUrl = api.BaseUrl,
                            method = api.Method,
                            pathTemplate = api.PathTemplate,
                            plannerParams = plan.ApiCall?.Params
                        }, JsonOptions),
                        ResponseStatusCode = exec.StatusCode,
                        ResponseSummaryJson = JsonSerializer.Serialize(new
                        {
                            success = exec.Success,
                            statusCode = exec.StatusCode
                        }, JsonOptions),
                        Error = exec.Error
                    }, ct);

                    yield return new StreamEvent("stage", Stage: userLang == "ar" ? "جارٍ إعداد الإجابة..." : "Preparing answer...");
                    yield return new StreamEvent("meta", sessionId, "API", [], null);

                    // Handle API execution failure gracefully
                    string answer;
                    if (!exec.Success || string.IsNullOrWhiteSpace(exec.ResponseBody))
                    {
                        answer = userLang == "ar"
                            ? "عذرًا، لم نتمكن من الحصول على البيانات المطلوبة حاليًا. يرجى المحاولة مرة أخرى لاحقًا."
                            : "Sorry, we couldn't retrieve the requested data at this time. Please try again later.";
                    }
                    else
                    {
                        // API answers are not streamed for now (requires different service method)
                        answer = await apiAnswer.AnswerFromApiResultAsync(req.Message, userLang, api.ApiName, exec.ResponseBody, api.ResponseHandlingNotes, ct);
                    }

                    // Save assistant response
                    await audit.AddMessageAsync(new ChatMessage
                    {
                        SessionId = sessionId,
                        Role = "assistant",
                        Text = answer
                    }, ct);

                    yield return new StreamEvent("chunk", Content: answer);
                    yield break;
                }
            }
            route = "GENERAL";
        }

        // GENERAL fallback - streamed
        yield return new StreamEvent("stage", Stage: userLang == "ar" ? "جارٍ إعداد الإجابة..." : "Preparing answer...");
        yield return new StreamEvent("meta", sessionId, "GENERAL", [], null);

        var generalFullResponse = new System.Text.StringBuilder();
        await foreach (var chunk in general.StreamAnswerGeneralAsync(req.Message, userLang, ct))
        {
            generalFullResponse.Append(chunk);
            yield return new StreamEvent("chunk", Content: chunk);
        }

        // Save assistant response after streaming completes
        await audit.AddMessageAsync(new ChatMessage
        {
            SessionId = sessionId,
            Role = "assistant",
            Text = generalFullResponse.ToString()
        }, ct);
    }

    private static string NormalizeRoute(string? route)
    {
        route = (route ?? "GENERAL").Trim().ToUpperInvariant();
        return route switch
        {
            "FAQ" => "FAQ",
            "RAG" => "RAG",
            "API" => "API",
            _ => "GENERAL"
        };
    }

    private static string? NormalizeLang(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return null;
        lang = lang.Trim().ToLowerInvariant();
        return lang switch
        {
            "ar" or "arabic" => "ar",
            "en" or "english" => "en",
            _ => null
        };
    }

    private static string DetectLang(string text)
    {
        // Simple heuristic: Arabic Unicode block
        foreach (var ch in text)
        {
            if (ch is >= '\u0600' and <= '\u06FF') return "ar";
        }
        return "en";
    }

    private static string? GetString(Dictionary<string, object> payload, string key)
        => payload.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static int? GetInt(Dictionary<string, object> payload, string key)
    {
        if (!payload.TryGetValue(key, out var v) || v is null) return null;
        if (v is long l) return (int)l;
        if (v is int i) return i;
        if (int.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }

    /// <summary>
    /// Hardcoded keyword detection for known GET APIs.
    /// Returns the pathTemplate fragment if a match is found, null otherwise.
    /// This runs as a safety net to catch planner misroutes for common queries.
    /// </summary>
    private static string? MatchGetApiKeywords(string message)
    {
        // Negation guard — if user is protesting or denying, don't match any keywords
        // "لم اسأل عن الصيدليات" / "شو دخل الصيدليات" should NOT match pharmacy
        string[] negationPrefixes = ["لم اسأل", "لم أسأل", "ما سألت", "شو دخل", "ما دخل",
            "I didn't ask", "I did not ask", "not about", "nothing to do"];
        if (negationPrefixes.Any(p => message.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return null;

        // Water schedule — timing/schedule queries about water
        // MUST include schedule-related words to avoid matching "عندي مشكلة مياه" (complaint)
        if (message.Contains("موعد المياه") || message.Contains("موعد الماء") || message.Contains("موعد الميه") ||
            message.Contains("جدول المياه") || message.Contains("جدول الماء") || message.Contains("جدول الميه") ||
            message.Contains("جدول توزيع") ||
            message.Contains("متى المياه") || message.Contains("متى الماء") || message.Contains("متى الميه") ||
            message.Contains("متى يجي الماء") || message.Contains("متى يجي الميه") || message.Contains("متى تجي المياه") ||
            message.Contains("متى ينزل الماء") || message.Contains("متى تنزل المياه") ||
            message.Contains("نزول المياه") || message.Contains("نزول الماء") ||
            message.Contains("water schedule", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("when is water", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("when does water", StringComparison.OrdinalIgnoreCase))
        {
            return "Water_s_plan";
        }

        // Pharmacies on duty
        if (message.Contains("صيدلية") || message.Contains("صيدليات") ||
            message.Contains("المناوبة") ||
            message.Contains("pharmacy", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("pharmacies", StringComparison.OrdinalIgnoreCase))
        {
            return "pharmacies_on_duty";
        }

        // Citizen fees / balance — personal balance lookup only
        // MUST exclude regulatory/procedure questions like "رسوم النفايات حسب النظام" or "براءة ذمة"
        // These are RAG questions about laws/procedures, not personal fee lookups
        string[] feeExclusions = ["حسب النظام", "نظام", "قانون", "براءة ذمة", "براءة", "شهادة",
            "إجراءات", "اجراءات", "كيف احصل", "كيف يمكنني", "according to", "regulation", "law", "clearance"];
        bool hasFeeExclusion = feeExclusions.Any(e => message.Contains(e, StringComparison.OrdinalIgnoreCase));
        if (!hasFeeExclusion &&
            (message.Contains("ذمم") || message.Contains("ذمت") ||
             message.Contains("كم رصيد") || message.Contains("رصيدي") ||
             message.Contains("كم رسوم") || message.Contains("رسومي") ||
             // Personal balance phrases
             message.Contains("كم ذم") || // covers كم ذممي، كم ذمتي
             message.Contains("بدي ادفع") || message.Contains("المبلغ الذي") ||
             message.Contains("my fees", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("my balance", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("how much do I owe", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("unpaid bills", StringComparison.OrdinalIgnoreCase)))
        {
            return "fees-by-customer-id";
        }

        return null;
    }

    private static readonly HashSet<string> ConfirmationWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "نعم", "اه", "ايوا", "موافق", "أكد", "تأكيد", "صحيح", "صح", "أوكي", "اوكي", "ماشي", "تمام",
        "yes", "ok", "okay", "confirm", "confirmed", "correct", "sure", "send", "submit", "go ahead"
    };

    private static readonly string[] RejectionPatterns =
    [
        "لا", "ليش", "ليه", "ما بدي", "لا أريد", "إلغاء", "غلط", "مش هيك", "ما بدي شكوى",
        "بطلت", "خلص", "خلاص", "كنسل", "الغي", "بلاش", "مش عايز", "ما اريد",
        "no", "cancel", "why", "don't want", "wrong", "not", "stop", "nevermind", "forget it"
    ];

    private static bool IsConfirmationMessage(string message)
    {
        var trimmed = message.Trim();
        // Check for rejection FIRST — reject takes priority over confirm
        if (IsRejectionMessage(trimmed)) return false;
        // Direct match
        if (ConfirmationWords.Contains(trimmed)) return true;
        // Check if message contains a confirmation word (for phrases like "حكيتلك نعم" or "والله نعم")
        foreach (var word in ConfirmationWords)
        {
            if (trimmed.Contains(word, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsRejectionMessage(string message)
    {
        var trimmed = message.Trim();
        foreach (var pattern in RejectionPatterns)
        {
            if (trimmed.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the message contains no Arabic characters and is likely a
    /// non-language input like a phone number, punctuation, or short English word
    /// that shouldn't override the conversation's language context.
    /// </summary>
    private static bool IsNonTextMessage(string message)
    {
        var trimmed = message.Trim();
        // Already has Arabic? Not a non-text message.
        if (trimmed.Any(c => c is >= '\u0600' and <= '\u06FF')) return false;
        // Purely numeric/punctuation (phone numbers, "?", "123", etc.)
        if (trimmed.All(c => char.IsDigit(c) || char.IsWhiteSpace(c) || c is '+' or '-' or '?' or '.' or '!' or '/' or '(' or ')'))
            return true;
        // Very short messages (1-3 chars) that are likely "ok", "no", "yes", "?"
        if (trimmed.Length <= 3) return true;
        return false;
    }

    private static bool IsConfirmationContext(IReadOnlyList<ConversationMessage> history)
    {
        var lastAssistant = history.LastOrDefault(m => m.Role == "assistant");
        return lastAssistant is not null &&
            (lastAssistant.Text.Contains("هل تريد تأكيد") || lastAssistant.Text.Contains("Do you want to confirm"));
    }

    /// <summary>
    /// Detects if the user is correcting a specific complaint field (not cancelling).
    /// "نوع الشكوى غلط" = correction. "لا قصدي..." = correction. Plain "لا" = rejection.
    /// "لا رقم هو 059..." = correction. "لا المنطقة X" = correction.
    /// </summary>
    private static bool IsCorrectionMessage(string message)
    {
        var trimmed = message.Trim();

        // Direct correction phrases (always correction)
        string[] correctionPhrases =
        [
            "قصدي", "بدي اغير", "بدي أغير", "غيّر", "عدّل", "صحح", "صلح",
            "change the", "change my", "update the", "update my", "correct the", "fix the"
        ];
        foreach (var phrase in correctionPhrases)
        {
            if (trimmed.Contains(phrase, StringComparison.OrdinalIgnoreCase)) return true;
        }

        // "غلط"/"wrong" + field reference = correction (e.g., "نوع الشكوى غلط")
        bool hasErrorWord = trimmed.Contains("غلط") || trimmed.Contains("wrong", StringComparison.OrdinalIgnoreCase);
        if (hasErrorWord)
        {
            string[] fieldWords =
            [
                "نوع", "المنطقة", "الموقع", "الرقم", "التفاصيل", "الشكوى", "المشكلة",
                "الجوال", "التلفون", "type", "location", "phone", "detail", "number"
            ];
            if (fieldWords.Any(f => trimmed.Contains(f, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        // "لا" + substantial content = likely correction, not just rejection
        // Patterns: "لا رقم هو 059..." / "لا المنطقة X" / "لا هو 059..."
        if (trimmed.StartsWith("لا ") && trimmed.Length > 10)
        {
            // Contains replacement indicator "هو"/"هي" (e.g., "لا رفم هو 059...")
            if (trimmed.Contains(" هو ") || trimmed.Contains(" هي ") ||
                trimmed.EndsWith(" هو") || trimmed.EndsWith(" هي"))
                return true;

            // Contains field-related words (including common typos)
            string[] fieldHints =
            [
                "رقم", "رفم", "الرقم", "موقع", "الموقع", "منطقة", "المنطقة",
                "نوع", "تفاصيل", "جوال", "تلفون", "phone", "number", "location"
            ];
            if (fieldHints.Any(f => trimmed.Contains(f, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Contains a phone number (7+ consecutive digits) → replacing phone
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"\d{7,}"))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Detects if the user is clearly initiating a NEW complaint.
    /// Used to override planner misroutes caused by previous conversation context.
    /// </summary>
    private static bool IsComplaintInitiation(string message)
    {
        var m = message.Trim();

        // Explicit complaint words
        if (m.Contains("شكوى") || m.Contains("شكاوى") || m.Contains("أشتكي") || m.Contains("اشتكي"))
            return true;

        // "عندي/في/عنا مشكلة" = reporting a problem
        if (m.Contains("عندي مشكلة") || m.Contains("عنا مشكلة") || m.Contains("في مشكلة") || m.Contains("فيه مشكلة"))
            return true;

        // "مشكلة" + complaint category type
        if (m.Contains("مشكلة"))
        {
            string[] complaintTypes = ["كهرباء", "مياه", "ماء", "صرف", "إنارة", "انارة", "طرق", "نفايات", "زبالة"];
            if (complaintTypes.Any(t => m.Contains(t)))
                return true;
        }

        // English equivalents
        if (m.Contains("complaint", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("complain", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("I have a problem", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("report a problem", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if the conversation is already in a complaint collection flow.
    /// Used to avoid overriding the planner when it's correctly handling multi-turn complaint collection.
    /// </summary>
    private static bool IsInComplaintFlow(IReadOnlyList<ConversationMessage> history)
    {
        if (history.Count == 0) return false;
        var lastAssistant = history.LastOrDefault(m => m.Role == "assistant");
        if (lastAssistant is null) return false;
        var text = lastAssistant.Text;

        // Already in complaint flow if last assistant was: confirmation card, asking for details
        return text.Contains("هل تريد تأكيد") || text.Contains("Do you want to confirm") ||
               text.Contains("تفاصيل الشكوى") || text.Contains("Complaint Details") ||
               text.Contains("ما هو موقع") || text.Contains("موقع المشكلة") ||
               text.Contains("رقم جوالك") || text.Contains("phone number") ||
               text.Contains("ما هي المشكلة") || text.Contains("What's the issue") ||
               text.Contains("ما هي تفاصيل") || text.Contains("Where is the problem") ||
               text.Contains("Where is the") || text.Contains("تفاصيل المشكلة");
    }

    /// <summary>
    /// Generates a follow-up question for complaint initiation when the planner was overridden.
    /// Shows available categories when the user hasn't specified a complaint type.
    /// </summary>
    private static string GenerateComplaintFollowUp(string message, string lang)
    {
        string[] types = ["كهرباء", "مياه", "ماء", "صرف", "إنارة", "انارة", "طرق", "نفايات", "زبالة",
                          "electricity", "water", "sewage", "road", "lighting", "garbage"];
        bool hasType = types.Any(t => message.Contains(t, StringComparison.OrdinalIgnoreCase));

        if (lang == "ar")
        {
            return hasType
                ? "ما هو موقع المشكلة؟ وما هو رقم جوالك للتواصل؟"
                : "بالتأكيد! يمكنك تقديم شكوى في أحد التصنيفات التالية:\n" +
                  "1. نفايات\n2. صرف صحي\n3. مياه\n4. إنارة\n5. طرق\n6. كهرباء\n\n" +
                  "ما هو نوع المشكلة التي تواجهها؟ وأين موقعها؟";
        }
        return hasType
            ? "Where is the problem located? And what's your phone number so we can follow up?"
            : "Sure! You can file a complaint in one of these categories:\n" +
              "1. Garbage\n2. Sewage\n3. Water\n4. Lighting\n5. Roads\n6. Electricity\n\n" +
              "What type of problem are you facing? And where is it located?";
    }

    /// <summary>
    /// Generates a complaint summary from body params when the planner didn't provide one.
    /// </summary>
    private static string GenerateComplaintSummary(Dictionary<string, object> bodyParams, string lang)
    {
        var categoryId = bodyParams.TryGetValue("CATEGORY_SUB_ID", out var cat) ? cat?.ToString() : null;
        var location = bodyParams.TryGetValue("LOCATION", out var loc) ? loc?.ToString() : null;
        var phone = bodyParams.TryGetValue("MOBILE_NO", out var mob) ? mob?.ToString() : null;
        var notes = bodyParams.TryGetValue("NOTES", out var n) ? n?.ToString() : null;

        var categoryName = categoryId switch
        {
            "1" => lang == "ar" ? "نفايات" : "Garbage",
            "2" => lang == "ar" ? "صرف صحي" : "Sewage",
            "3" => lang == "ar" ? "مياه" : "Water",
            "4" => lang == "ar" ? "إنارة" : "Lighting",
            "5" => lang == "ar" ? "طرق" : "Roads",
            "6" => lang == "ar" ? "كهرباء" : "Electricity",
            _ => categoryId ?? (lang == "ar" ? "غير محدد" : "Unknown")
        };

        if (lang == "ar")
            return $"📋 تفاصيل الشكوى:\n• نوع المشكلة: {categoryName}\n• الموقع: {location ?? "غير محدد"}\n• رقم الجوال: {phone ?? "غير محدد"}\n• تفاصيل: {notes ?? "غير محدد"}";

        return $"📋 Complaint Details:\n• Problem type: {categoryName}\n• Location: {location ?? "Not specified"}\n• Phone: {phone ?? "Not specified"}\n• Details: {notes ?? "Not specified"}";
    }

    /// <summary>
    /// Detects greetings and chitchat that should ALWAYS route to GENERAL,
    /// regardless of conversation history or planner decision.
    /// </summary>
    private static bool IsGreeting(string message)
    {
        var m = message.Trim();
        // Remove trailing punctuation for matching
        var clean = m.TrimEnd('!', '?', '؟', '.', '،', ',', ' ');

        string[] greetings =
        [
            "صباح الخير", "مساء الخير", "السلام عليكم", "وعليكم السلام",
            "مرحبا", "مرحبه", "أهلا", "اهلا", "هلا", "يا هلا",
            "كيف حالك", "كيف الحال", "شلونك", "شخبارك",
            "من انت", "من أنت", "شو اسمك", "مين انت",
            "محترم", "شكرا", "يعطيك العافية",
            "hi", "hello", "hey", "good morning", "good evening",
            "how are you", "who are you", "what's your name",
            "thanks", "thank you"
        ];

        foreach (var g in greetings)
        {
            if (clean.Equals(g, StringComparison.OrdinalIgnoreCase)) return true;
            // Allow slight variations like "صباح الخير يا خليلي" (greeting + name)
            if (clean.StartsWith(g, StringComparison.OrdinalIgnoreCase) && clean.Length < g.Length + 15) return true;
        }

        return false;
    }

    /// <summary>
    /// Detects user protests about wrong responses — "شو دخل الصيدليات" / "لم اسأل عن X"
    /// These should always route to GENERAL with an apology.
    /// </summary>
    private static bool IsProtestMessage(string message)
    {
        var m = message.Trim();
        string[] protestPatterns =
        [
            "شو دخل", "ما دخل", "لم اسأل", "لم أسأل", "ما سألت", "مش هيك",
            "مش هاد سؤالي", "ما هاد سؤالي", "هاد مش", "ليش بتحكيلي",
            "I didn't ask", "I did not ask", "that's not what I asked",
            "nothing to do with", "what does that have to do"
        ];
        return protestPatterns.Any(p => m.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true if the message looks like a question or service request (not a greeting or personal statement).
    /// Used to gate the FAQ safety net — only override GENERAL→FAQ for actual questions.
    /// </summary>
    private static bool IsQuestionOrRequest(string message)
    {
        var m = message.Trim();

        // Question mark is a strong signal
        if (m.Contains('?') || m.Contains('؟')) return true;

        // Arabic question/request words
        string[] arQuestionWords = ["كيف", "ما هي", "ما هو", "ماهي", "ماهو", "شو", "ايش", "إيش",
            "وين", "أين", "فين", "متى", "هل", "ليش", "ليه", "لماذا", "كم",
            "أريد", "اريد", "بدي", "ممكن", "عايز", "أبغى",
            "اشتراك", "تسجيل", "تقديم", "طلب", "شكوى", "اشتكي"];

        // English question/request words
        string[] enQuestionWords = ["how", "what", "where", "when", "why", "which", "can i", "do you",
            "is there", "are there", "i want", "i need", "register", "subscribe", "apply", "complaint"];

        foreach (var w in arQuestionWords)
        {
            if (m.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
        }
        foreach (var w in enQuestionWords)
        {
            if (m.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// Server-side correction enforcement: when user corrects a complaint field,
    /// detect what they're changing and update the body params.
    /// Called when IsCorrectionMessage is true and we have body params from the planner.
    /// </summary>
    private static void ApplyCorrectionToBodyParams(string message, Dictionary<string, object> bodyParams, IReadOnlyList<ConversationMessage> history)
    {
        var m = message.Trim();

        // Detect complaint type correction
        var typeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["نفايات"] = "1", ["زبالة"] = "1", ["garbage"] = "1",
            ["صرف صحي"] = "2", ["صرف"] = "2", ["sewage"] = "2", ["drainage"] = "2",
            ["مياه"] = "3", ["ماء"] = "3", ["water"] = "3",
            ["إنارة"] = "4", ["انارة"] = "4", ["lighting"] = "4",
            ["طرق"] = "5", ["شوارع"] = "5", ["roads"] = "5", ["road"] = "5",
            ["كهرباء"] = "6", ["كهربا"] = "6", ["electricity"] = "6"
        };

        // Check if user is correcting the complaint type
        bool typeCorrection = m.Contains("نوع") || m.Contains("المشكلة") || m.Contains("الشكوى") ||
                              m.Contains("type", StringComparison.OrdinalIgnoreCase) || m.Contains("مش ");
        if (typeCorrection)
        {
            foreach (var kv in typeMap)
            {
                if (m.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    bodyParams["CATEGORY_SUB_ID"] = kv.Value;
                    // Also update NOTES if it just says the old type name
                    if (bodyParams.TryGetValue("NOTES", out var notes))
                    {
                        var notesStr = notes?.ToString() ?? "";
                        // If NOTES is just "مشكلة [old type]", update it
                        foreach (var oldType in typeMap.Keys)
                        {
                            if (notesStr.Contains(oldType, StringComparison.OrdinalIgnoreCase))
                            {
                                bodyParams["NOTES"] = notesStr.Replace(oldType, kv.Key, StringComparison.OrdinalIgnoreCase);
                                break;
                            }
                        }
                    }
                    break;
                }
            }
        }

        // Check if user is correcting the phone number
        var phoneRegex = System.Text.RegularExpressions.Regex.Match(m, @"(\d{7,15})");
        if (phoneRegex.Success && (m.Contains("رقم") || m.Contains("رفم") || m.Contains("هاتف") ||
            m.Contains("جوال") || m.Contains("تلفون") || m.Contains("phone", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("number", StringComparison.OrdinalIgnoreCase) || m.StartsWith("لا")))
        {
            bodyParams["MOBILE_NO"] = phoneRegex.Groups[1].Value;
        }

        // Check if user is correcting the location
        if (m.Contains("موقع") || m.Contains("منطقة") || m.Contains("شارع") || m.Contains("حي") ||
            m.Contains("location", StringComparison.OrdinalIgnoreCase))
        {
            // Try to extract the new location — take the substantial part after correction keywords
            string[] correctionPrefixes = ["قصدي", "هو ", "هي ", "الموقع ", "المنطقة ", "في "];
            foreach (var prefix in correctionPrefixes)
            {
                var idx = m.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var newLocation = m[(idx + prefix.Length)..].Trim();
                    // Remove trailing digits (phone number might follow)
                    newLocation = System.Text.RegularExpressions.Regex.Replace(newLocation, @"\s*\d{7,15}\s*$", "").Trim();
                    if (newLocation.Length > 2)
                    {
                        bodyParams["LOCATION"] = newLocation;
                        break;
                    }
                }
            }
        }
    }
}

