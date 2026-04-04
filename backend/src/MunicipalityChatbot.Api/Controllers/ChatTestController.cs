using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Application.Models;
using MunicipalityChatbot.Domain.Entities;
using MunicipalityChatbot.Infrastructure.Config;

namespace MunicipalityChatbot.Api.Controllers;

[ApiController]
[Route("api/chat-test")]
public sealed class ChatTestController(
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
    ILogger<ChatTestController> logger
) : ControllerBase
{
    private const string AllowedRoles = $"{EmployeeRoles.EmployeeAdmin},{EmployeeRoles.EmployeeEditor}";

    private static readonly List<TestQuestion> DefaultTestQuestions =
    [
        // ===== GREETINGS =====
        new("مرحبا", "ar", "GENERAL", "Greeting - Arabic"),
        new("Hello", "en", "GENERAL", "Greeting - English"),
        new("صباح الخير", "ar", "GENERAL", "Good morning - Arabic"),
        new("كيف حالك", "ar", "GENERAL", "How are you - Arabic"),

        // ===== FAQ =====
        new("ما هي ساعات عمل البلدية؟", "ar", "FAQ", "FAQ: Working hours - Arabic"),
        new("What are the municipality working hours?", "en", "FAQ", "FAQ: Working hours - English"),
        new("كيف أسجل في تطبيق البلدية؟", "ar", "FAQ", "FAQ: App registration - Arabic"),
        new("أريد التحدث مع موظف", "ar", "FAQ", "FAQ: Talk to employee - Arabic"),

        // ===== API: PHARMACIES =====
        new("ما هي صيدليات المناوبة اليوم؟", "ar", "API", "API: Pharmacies on duty - Arabic"),
        new("Which pharmacies are on duty today?", "en", "API", "API: Pharmacies on duty - English"),
        new("وين أقرب صيدلية؟", "ar", "API", "API: Nearest pharmacy - colloquial"),

        // ===== API: WATER SCHEDULE =====
        new("ما هو جدول توزيع المياه لهذا اليوم؟", "ar", "API", "API: Water schedule general"),
        new("What is the water schedule for today?", "en", "API", "API: Water schedule - English"),
        new("متى موعد المياه في منطقة وادي الجوز؟", "ar", "API", "API: Water schedule specific area"),
        new("متى يجي الماء لمنطقة الفحص؟", "ar", "API", "API: Water schedule colloquial"),
        new("متى موعد المياه لمنطقة لوزا", "ar", "API", "API: Water schedule Luza area"),
        new("متى ينزل الماء عندنا", "ar", "API", "API: Water schedule 'when comes'"),
        new("موعد المياه", "ar", "API", "API: Water schedule short"),
        new("ما هو جدول توزيع المايه", "ar", "API", "API: Water schedule with typo"),

        // ===== API: COMPLAINT INITIATION =====
        new("أريد تقديم شكوى", "ar", "API", "Complaint: Initiation - Arabic"),
        new("I want to submit a complaint", "en", "API", "Complaint: Initiation - English"),
        new("عندي مشكلة كهرباء في شارع الملك فيصل", "ar", "API", "Complaint: With details"),
        new("عندي مشكلة مياه", "ar", "API", "Complaint: Water problem (not schedule)"),
        new("عندي مشكلة كهرباء", "ar", "API", "Complaint: Electricity no location"),

        // ===== COMPLAINT: UNKNOWN CATEGORY =====
        new("عندي مشكلة مرور", "ar", "API|GENERAL", "Complaint: Unknown category 'مرور'"),
        new("بدي اشتكي عن ضوضاء", "ar", "API|GENERAL", "Complaint: Unknown category 'ضوضاء'"),

        // ===== COMPLAINT CANCELLATION =====
        new("ما بدي ابعت شكوى", "ar", "GENERAL", "Complaint: Cancellation 'ما بدي'"),
        new("بطلت ابعت شكوى", "ar", "GENERAL", "Complaint: Cancellation 'بطلت'"),

        // ===== CITIZEN FEES =====
        new("شو الرسوم المترتبة علي؟", "ar", "API", "Fees: Personal fees query (f_type=1)"),
        new("كم ذممي", "ar", "API", "Fees: Colloquial unpaid (f_type=1)"),
        new("بدي ادفع رسوم", "ar", "API", "Fees: Want to pay (f_type=1)"),
        new("هل عندي رسوم غير مدفوعة", "ar", "API", "Fees: Unpaid fees explicit (f_type=1)"),
        new("شو دفعت من رسوم", "ar", "API", "Fees: Paid fees (f_type=2)"),
        new("بدي اعرف الرسوم المجدولة", "ar", "API", "Fees: Scheduled fees (f_type=3)"),
        new("عندي رسوم مجمدة؟", "ar", "API", "Fees: Frozen fees (f_type=4)"),
        new("بدي اشوف حسابي", "ar", "API", "Fees: General account inquiry"),

        // ===== CITIZEN FEES VS RAG (should NOT route to fees API) =====
        new("رسوم النفايات حسب النظام", "ar", "RAG|GENERAL", "Fees vs RAG: Regulatory question"),
        new("براءة ذمة", "ar", "RAG|GENERAL", "Fees vs RAG: Clearance certificate"),
        new("كيف احصل على براءة ذمة", "ar", "RAG|GENERAL", "Fees vs RAG: How to get clearance"),

        // ===== SERVICE REQUESTS (NOT complaints) =====
        new("اريد تقديم طلب اشتراك مياه", "ar", "RAG|GENERAL", "Service: Water subscription"),
        new("طلب اشتراك كهرباء", "ar", "RAG|GENERAL", "Service: Electricity subscription"),
        new("I want to apply for a water subscription", "en", "RAG|GENERAL", "Service: Water subscription - English"),

        // ===== OUT-OF-SCOPE =====
        new("أريد رخصة قيادة", "ar", "GENERAL", "Out-of-scope: Driver's license"),
        new("كيف أستخرج جواز سفر؟", "ar", "GENERAL", "Out-of-scope: Passport"),
        new("How do I get a driver's license?", "en", "GENERAL", "Out-of-scope: Driver's license - English"),
        new("بخصوص عدم المحكومية", "ar", "GENERAL", "Out-of-scope: Criminal record"),

        // ===== RAG =====
        new("ما هي رسوم رخصة البناء؟", "ar", "RAG", "RAG: Building permit fees"),
        new("ما هي متطلبات رخصة المهن؟", "ar", "RAG", "RAG: Business license requirements"),
        new("كيف اقدم اشتراك مياه؟", "ar", "RAG|GENERAL", "RAG: Water subscription procedure"),
        new("كيف اقدم طلب اشتراك كهرباء", "ar", "RAG|GENERAL", "RAG: Electricity subscription procedure"),

        // ===== PROTEST MESSAGES =====
        new("لم اسأل عن الصيدليات", "ar", "GENERAL", "Protest: Didn't ask about pharmacies"),
        new("شو دخل المياه بسؤالي", "ar", "GENERAL", "Protest: What does water have to do with it"),

        // ===== FALSE FAQ MATCHES =====
        new("ما هو موقع البلدية", "ar", "FAQ|GENERAL|RAG", "Edge: Municipality location (not working hours FAQ)"),
        new("انا اسمي اسامة", "ar", "GENERAL", "Edge: Personal statement (not FAQ)"),

        // ===== GENERAL KNOWLEDGE =====
        new("ما هو رقم هاتف البلدية؟", "ar", "FAQ|GENERAL", "General: Municipality phone"),
        new("من هو رئيس البلدية", "ar", "RAG|GENERAL", "General: Who is the mayor"),
        new("كم عمرك؟", "ar", "GENERAL", "General: Chitchat - how old"),

        // ===== CONTEXT STICKINESS (single-turn, verify no bleed) =====
        new("لكن منطقتي في الجدول ولم تصلني", "ar", "API|GENERAL", "Edge: Area in schedule but no water"),

        // ===== PDF LAWS: BUILDING TAX (ضريبة الابنية رقم 11 + تعديل 12) =====
        new("ما هو قانون ضريبة الأبنية؟", "ar", "RAG", "PDF: Building tax law overview"),
        new("ما هي نسبة ضريبة الأبنية؟", "ar", "RAG", "PDF: Building tax rate"),
        new("ما هي التعديلات على قانون ضريبة الابنية رقم 11؟", "ar", "RAG", "PDF: Building tax law amendments"),
        new("من يدفع ضريبة الأبنية؟", "ar", "RAG", "PDF: Who pays building tax"),
        new("هل هناك إعفاءات من ضريبة الأبنية؟", "ar", "RAG", "PDF: Building tax exemptions"),

        // ===== PDF LAWS: CITY PLANNING (تنظيم المدن والقرى رقم 79) =====
        new("ما هو قانون تنظيم المدن والقرى؟", "ar", "RAG", "PDF: City planning law overview"),
        new("ما هي شروط البناء حسب قانون تنظيم المدن؟", "ar", "RAG", "PDF: Building conditions under planning law"),
        new("ما هي صلاحيات لجنة التنظيم المحلية؟", "ar", "RAG", "PDF: Local planning committee powers"),
        new("ما هي العقوبات على مخالفات البناء؟", "ar", "RAG", "PDF: Building violation penalties"),
        new("هل يمكن الاعتراض على قرار لجنة التنظيم؟", "ar", "RAG", "PDF: Appeal planning committee decision"),

        // ===== PDF LAWS: EXPROPRIATION (الاستملاك رقم 2) =====
        new("ما هو قانون الاستملاك؟", "ar", "RAG", "PDF: Expropriation law overview"),
        new("متى يحق للبلدية استملاك أراضي؟", "ar", "RAG", "PDF: When can municipality expropriate"),
        new("كيف يتم تعويض صاحب الأرض المستملكة؟", "ar", "RAG", "PDF: Expropriation compensation"),
        new("ما هي إجراءات الاستملاك للمنفعة العامة؟", "ar", "RAG", "PDF: Public benefit expropriation procedures"),

        // ===== PDF LAWS: ELECTRICITY (الكهرباء رقم 13) =====
        new("ما هو قانون الكهرباء الفلسطيني؟", "ar", "RAG", "PDF: Electricity law overview"),
        new("ما هي صلاحيات سلطة الطاقة؟", "ar", "RAG", "PDF: Energy authority powers"),
        new("ما هي شروط الحصول على رخصة توزيع كهرباء؟", "ar", "RAG", "PDF: Electricity distribution license"),
        new("ما هي حقوق المستهلك في قانون الكهرباء؟", "ar", "RAG", "PDF: Consumer rights electricity law"),
        new("ما هي عقوبات سرقة الكهرباء؟", "ar", "RAG", "PDF: Electricity theft penalties"),

        // ===== PDF LAWS: LOCAL AUTHORITIES (الهيئات المحلية رقم 1 لسنة 1997) =====
        new("ما هو قانون الهيئات المحلية الفلسطينية؟", "ar", "RAG", "PDF: Local authorities law overview"),
        new("كيف يتم انتخاب رئيس البلدية؟", "ar", "RAG", "PDF: Mayor election process"),
        new("ما هي صلاحيات المجلس البلدي؟", "ar", "RAG", "PDF: Municipal council powers"),
        new("ما هي شروط الترشح لرئاسة البلدية؟", "ar", "RAG", "PDF: Mayor candidacy requirements"),
        new("متى يتم حل المجلس البلدي؟", "ar", "RAG", "PDF: When is council dissolved"),
        new("ما هي مصادر دخل الهيئات المحلية؟", "ar", "RAG", "PDF: Local authority revenue sources"),

        // ===== PDF LAWS: CRAFTS & INDUSTRIES (الحرف والصناعات رقم 16) =====
        new("ما هي الحرف المصنفة حسب القانون؟", "ar", "RAG", "PDF: Classified crafts by law"),
        new("ما هي شروط ترخيص المصانع؟", "ar", "RAG", "PDF: Factory licensing requirements"),
        new("هل تحتاج الورش لترخيص من البلدية؟", "ar", "RAG", "PDF: Workshop licensing from municipality"),

        // ===== PDF REGULATIONS: BUILDING & PLANNING (نظام الابنية رقم 6) =====
        new("ما هو نظام الأبنية والتنظيم للهيئات المحلية؟", "ar", "RAG", "PDF: Building regulation overview"),
        new("ما هي نسبة البناء المسموحة؟", "ar", "RAG", "PDF: Allowed building percentage"),
        new("ما هو الارتداد المطلوب للبناء؟", "ar", "RAG", "PDF: Required building setback"),
        new("كم طابق مسموح بناؤها؟", "ar", "RAG", "PDF: Maximum allowed floors"),
        new("ما هي شروط بناء الطابق السفلي؟", "ar", "RAG", "PDF: Basement construction requirements"),
        new("ما هي شروط المواقف في الأبنية الجديدة؟", "ar", "RAG", "PDF: Parking requirements new buildings"),
        new("ما هي شروط بناء الأسوار؟", "ar", "RAG", "PDF: Fence construction requirements"),
        new("ما هي أنواع استعمالات الأراضي؟", "ar", "RAG", "PDF: Land use categories"),

        // ===== PDF REGULATIONS: WASTE FEES (نظام منع المكارة ورسوم النفايات رقم 4) =====
        new("ما هي رسوم جمع النفايات؟", "ar", "RAG", "PDF: Waste collection fees"),
        new("كم رسوم النفايات للمنازل؟", "ar", "RAG", "PDF: Residential waste fees"),
        new("كم رسوم النفايات للمحلات التجارية؟", "ar", "RAG", "PDF: Commercial waste fees"),
        new("ما هي عقوبة رمي النفايات في الشارع؟", "ar", "RAG", "PDF: Street littering penalty"),
        new("ما هو نظام منع المكارة؟", "ar", "RAG", "PDF: Anti-nuisance regulation overview"),
        new("ما هي أنواع المكارة المحظورة؟", "ar", "RAG", "PDF: Prohibited nuisances types"),

        // ===== PDF REGULATIONS: VEHICLE PARKING (نظام مواقف المركبات رقم 3) =====
        new("ما هو نظام مواقف المركبات؟", "ar", "RAG", "PDF: Vehicle parking regulation overview"),
        new("كم رسوم مواقف السيارات في البلدية؟", "ar", "RAG", "PDF: Municipal parking fees"),
        new("ما هي مخالفات الوقوف الخاطئ؟", "ar", "RAG", "PDF: Wrong parking violations"),
        new("هل يوجد مواقف مجانية؟", "ar", "RAG", "PDF: Free parking availability"),

        // ===== PDF CROSS-TOPIC QUESTIONS =====
        new("ما الفرق بين رخصة البناء ورخصة المهن؟", "ar", "RAG", "PDF: Building vs business license difference"),
        new("ما هي القوانين التي تنظم عمل البلدية؟", "ar", "RAG", "PDF: Laws governing municipality work"),
        new("ما هي حقوق المواطن تجاه البلدية؟", "ar", "RAG", "PDF: Citizen rights towards municipality"),
    ];

    [HttpPost("run")]
    [Authorize(Roles = AllowedRoles)]
    public async Task<ActionResult<ChatTestResult>> RunTests(
        [FromBody] ChatTestRequest? request = null,
        CancellationToken ct = default)
    {
        var questions = request?.Questions?.Count > 0
            ? request.Questions
            : DefaultTestQuestions;

        var results = new List<TestQuestionResult>();
        var totalSw = Stopwatch.StartNew();

        await qdrant.EnsureCollectionAsync(qdrantOptions.VectorSize, ct);
        var apiDefs = await apis.ListAllowedInChatAsync(ct);

        for (int i = 0; i < questions.Count; i++)
        {
            var result = await RunSingleTestAsync(questions[i], apiDefs, request?.UserToken, ct);
            results.Add(result);

            // Buffer delay between questions to avoid 429 rate limiting from LLM provider
            if (i < questions.Count - 1)
                await Task.Delay(1500, ct);
        }

        totalSw.Stop();

        var summary = new ChatTestSummary
        {
            TotalQuestions = results.Count,
            RoutingCorrect = results.Count(r => r.RoutingCorrect),
            RoutingWrong = results.Count(r => !r.RoutingCorrect),
            RouteDistribution = results.GroupBy(r => r.ActualRoute).ToDictionary(g => g.Key, g => g.Count()),
            TotalTimeMs = totalSw.ElapsedMilliseconds,
            AverageTimeMs = results.Count > 0 ? (long)results.Average(r => r.TotalTimeMs) : 0,
            Errors = results.Where(r => r.Error != null).Select(r => new TestError(r.Question, r.Error!)).ToList()
        };

        return Ok(new ChatTestResult(summary, results));
    }

    [HttpPost("test-fees")]
    [Authorize(Roles = AllowedRoles)]
    public async Task<ActionResult> TestFeesApi(
        [FromBody] TestFeesRequest? request = null,
        CancellationToken ct = default)
    {
        var userToken = request?.UserToken;
        if (string.IsNullOrWhiteSpace(userToken))
            return Ok(new { results = Array.Empty<object>(), error = "User token is required." });

        // Build URL candidates to try
        var hosts = new[] { "192.168.100.2", "egate.hebron-city.ps" };
        var schemes = new[] { "https", "http" };
        var ports = new[] { "", ":8282", ":443", ":80", ":8080", ":8181", ":9090", ":5000" };
        var paths = new[]
        {
            "/api/e-payments/fees-by-customer-id?f_type=1",
            "/e-payments/fees-by-customer-id?f_type=1",
            "/api/e-payments/fees-by-customer-id/?f_type=1",
            "/fees-by-customer-id?f_type=1",
        };

        // Use the DI-provided HttpClient (has SSL bypass configured)
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { Timeout = TimeSpan.FromSeconds(5) };

        var results = new List<object>();
        var totalSw = Stopwatch.StartNew();

        foreach (var scheme in schemes)
        foreach (var host in hosts)
        foreach (var port in ports)
        foreach (var path in paths)
        {
            // Skip redundant combos: https with explicit :443, http with :80
            if (scheme == "https" && port == ":443") continue;
            if (scheme == "http" && port == ":80") continue;

            var url = $"{scheme}://{host}{port}{path}";
            var sw = Stopwatch.StartNew();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));

                using var resp = await httpClient.SendAsync(req, cts.Token);
                var body = await resp.Content.ReadAsStringAsync(cts.Token);
                sw.Stop();

                var statusCode = (int)resp.StatusCode;
                var isSuccess = statusCode is >= 200 and < 300;

                results.Add(new
                {
                    url,
                    statusCode,
                    success = isSuccess,
                    timeMs = sw.ElapsedMilliseconds,
                    body = body.Length > 500 ? body[..500] + "..." : body
                });

                // If we found a working one, still continue to show all results
            }
            catch (TaskCanceledException)
            {
                sw.Stop();
                results.Add(new { url, statusCode = (int?)null, success = false, timeMs = sw.ElapsedMilliseconds, body = "TIMEOUT (5s)" });
            }
            catch (Exception ex)
            {
                sw.Stop();
                results.Add(new { url, statusCode = (int?)null, success = false, timeMs = sw.ElapsedMilliseconds, body = $"{ex.GetType().Name}: {ex.Message}" });
            }
        }

        totalSw.Stop();

        // Sort: successes first, then by status code (200s first, then 4xx, then errors)
        var sorted = results.Cast<dynamic>()
            .OrderByDescending(r => (bool)r.success)
            .ThenBy(r => r.statusCode == null ? 999 : (int)r.statusCode)
            .ToList();

        return Ok(new { results = sorted, totalTimeMs = totalSw.ElapsedMilliseconds });
    }

    [HttpGet("default-questions")]
    [Authorize(Roles = AllowedRoles)]
    public ActionResult<List<TestQuestion>> GetDefaultQuestions()
        => Ok(DefaultTestQuestions);

    private async Task<TestQuestionResult> RunSingleTestAsync(
        TestQuestion q,
        IReadOnlyList<ApiDefinition> apiDefs,
        string? userToken,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = new TestQuestionResult
        {
            Question = q.Message,
            ExpectedRoute = q.ExpectedRoute,
            Category = q.Category,
            Lang = q.Lang
        };

        try
        {
            // Step 1: Detect language
            var detectedLang = DetectLang(q.Message);
            result.DetectedLang = detectedLang;
            var userLang = q.Lang ?? detectedLang;

            // Step 2: Embed
            var embedSw = Stopwatch.StartNew();
            var queryVec = await embeddings.EmbedAsync(q.Message, ct);
            embedSw.Stop();
            result.EmbedTimeMs = embedSw.ElapsedMilliseconds;

            // Step 3: Vector search
            var searchSw = Stopwatch.StartNew();
            var qdrantLang = userLang == "ar" ? "AR" : "EN";
            var faqHits = await qdrant.SearchAsync("faq", queryVec, topK: 5, language: qdrantLang, ct);
            var docHits = await qdrant.SearchAsync("doc_chunk", queryVec, topK: 8, language: null, ct);
            var websiteHits = await qdrant.SearchAsync("website", queryVec, topK: 5, language: null, ct);
            searchSw.Stop();
            result.SearchTimeMs = searchSw.ElapsedMilliseconds;

            // Capture FAQ candidates
            result.FaqCandidates = faqHits.Select(h => new FaqCandidateInfo
            {
                FaqId = GetString(h.Payload, "faqId") ?? h.PointId,
                Title = GetString(h.Payload, "title"),
                Question = GetString(h.Payload, "question"),
                Score = h.Score,
                Language = GetString(h.Payload, "language")
            }).ToList();

            // Capture doc chunk candidates
            var docChunkInfos = docHits.Select(h => new ChunkCandidateInfo
            {
                ChunkId = GetString(h.Payload, "chunkId") ?? h.PointId,
                Filename = GetString(h.Payload, "filename"),
                TextPreview = Truncate(GetString(h.Payload, "text"), 200),
                Score = h.Score,
                Source = "document"
            }).ToList();

            var websiteChunkInfos = websiteHits.Select(h => new ChunkCandidateInfo
            {
                ChunkId = GetString(h.Payload, "chunkId") ?? h.PointId,
                Filename = GetString(h.Payload, "url"),
                TextPreview = Truncate(GetString(h.Payload, "text"), 200),
                Score = h.Score,
                Source = "website"
            }).ToList();

            result.DocChunkCandidates = docChunkInfos.Concat(websiteChunkInfos)
                .OrderByDescending(c => c.Score)
                .Take(10)
                .ToList();

            // Step 4: Planner
            var faqCandidates = faqHits.Select(h => new
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

            var allDocCandidates = docChunkCandidates.Concat(websiteChunkCandidates)
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
                UserMessage = q.Message,
                UserLang = userLang,
                FaqCandidates = faqCandidates,
                DocChunkCandidates = allDocCandidates,
                ApiDefinitions = apiDefsForPlanner,
                SessionState = new { },
                ConversationHistory = null
            };

            var planSw = Stopwatch.StartNew();
            var plan = await planner.PlanAsync(plannerInput, ct);
            planSw.Stop();
            result.PlannerTimeMs = planSw.ElapsedMilliseconds;

            result.ActualRoute = NormalizeRoute(plan.Route);
            result.Confidence = plan.Confidence;
            result.PlannerRawJson = plan.RawJson;
            result.FollowUpQuestion = plan.FollowUpQuestion;
            result.SelectedFaqId = plan.SelectedFaqId;
            result.SelectedChunkIds = plan.SelectedChunkIds;
            result.RequiresConfirmation = plan.RequiresConfirmation;
            result.PendingSubmissionSummary = plan.PendingSubmissionSummary;

            if (plan.ApiCall != null)
            {
                result.ApiCallInfo = new ApiCallInfo
                {
                    ApiId = plan.ApiCall.ApiId,
                    ApiName = apiDefs.FirstOrDefault(a => a.ApiId.ToString() == plan.ApiCall.ApiId)?.ApiName,
                    Params = plan.ApiCall.Params
                };
            }

            // Step 5: Execute the route to get final answer
            var answerSw = Stopwatch.StartNew();
            var route = result.ActualRoute;

            if (route == "FAQ" && !string.IsNullOrWhiteSpace(plan.SelectedFaqId))
            {
                if (Guid.TryParse(plan.SelectedFaqId, out var faqId))
                {
                    var faq = await faqs.GetByIdAsync(faqId, ct);
                    result.FinalAnswer = faq?.Answer ?? "[FAQ not found in DB]";
                }
                else result.FinalAnswer = "[Invalid FAQ ID]";
            }
            else if (route == "RAG" && plan.SelectedChunkIds is { Count: > 0 })
            {
                var chunkIds = plan.SelectedChunkIds
                    .Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
                    .Where(g => g != Guid.Empty)
                    .ToArray();
                var dbChunks = await docs.GetChunksByIdsAsync(chunkIds, ct);
                var chunks = new List<MunicipalityChatbot.Domain.Entities.DocumentChunk>(dbChunks);

                // Fallback: website chunks are only in Qdrant, not PostgreSQL
                if (chunks.Count < chunkIds.Length)
                {
                    var foundIds = chunks.Select(c => c.ChunkId).ToHashSet();
                    foreach (var candidate in allDocCandidates)
                    {
                        var dyn = (dynamic)candidate;
                        var candidateId = (string)dyn.chunkId;
                        if (Guid.TryParse(candidateId, out var cid) && chunkIds.Contains(cid) && !foundIds.Contains(cid))
                        {
                            var text = (string?)dyn.text;
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                chunks.Add(new MunicipalityChatbot.Domain.Entities.DocumentChunk
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
                }

                if (chunks.Count > 0)
                    result.FinalAnswer = await rag.AnswerFromChunksAsync(q.Message, userLang, chunks, ct);
                else
                    result.FinalAnswer = "[No chunks found in DB for selected IDs]";
            }
            else if (route == "API")
            {
                if (!string.IsNullOrWhiteSpace(plan.FollowUpQuestion))
                {
                    result.FinalAnswer = $"[Follow-up question] {plan.FollowUpQuestion}";
                }
                else if (plan.ApiCall?.ApiId is { } apiIdStr && Guid.TryParse(apiIdStr, out var parsedApiId))
                {
                    var api = apiDefs.FirstOrDefault(a => a.ApiId == parsedApiId);
                    if (api != null)
                    {
                        if (api.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                        {
                            // Don't actually submit complaints in test mode
                            result.FinalAnswer = plan.RequiresConfirmation
                                ? $"[POST API - Confirmation card] {plan.PendingSubmissionSummary}"
                                : "[POST API - Would execute but skipped in test mode]";
                        }
                        else
                        {
                            // Check if API requires user token but none provided
                            if (api.AuthType.Equals("UserToken", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(userToken))
                            {
                                result.FinalAnswer = "[Requires UserToken - provide token in test settings to execute]";
                            }
                            else
                            {
                                // GET APIs are safe to execute
                                var exec = await apiExec.ExecuteAsync(api, plan.ApiCall, ct, userToken);
                                if (exec.Success && !string.IsNullOrWhiteSpace(exec.ResponseBody))
                                {
                                    result.ApiRawResponse = Truncate(exec.ResponseBody, 2000);
                                    result.FinalAnswer = await apiAnswer.AnswerFromApiResultAsync(
                                        q.Message, userLang, api.ApiName, exec.ResponseBody, api.ResponseHandlingNotes, ct);
                                }
                                else
                                {
                                    result.FinalAnswer = $"[API call failed: {exec.Error ?? "empty response"}, status: {exec.StatusCode}]";
                                }
                            }
                        }
                    }
                    else result.FinalAnswer = "[API not found]";
                }
                else result.FinalAnswer = "[No API ID in planner result]";
            }
            else
            {
                result.FinalAnswer = await general.AnswerGeneralAsync(q.Message, userLang, ct);
            }
            answerSw.Stop();
            result.AnswerTimeMs = answerSw.ElapsedMilliseconds;

            // Check routing correctness
            var expectedRoutes = q.ExpectedRoute.Split('|');
            result.RoutingCorrect = expectedRoutes.Contains(result.ActualRoute);
        }
        catch (Exception ex)
        {
            result.Error = $"{ex.GetType().Name}: {ex.Message}";
            result.ActualRoute = "ERROR";
            result.RoutingCorrect = false;
            logger.LogWarning(ex, "Test question failed: {Question}", q.Message);
        }
        finally
        {
            sw.Stop();
            result.TotalTimeMs = sw.ElapsedMilliseconds;
        }

        return result;
    }

    private static string NormalizeRoute(string? route)
    {
        route = (route ?? "GENERAL").Trim().ToUpperInvariant();
        return route switch { "FAQ" => "FAQ", "RAG" => "RAG", "API" => "API", _ => "GENERAL" };
    }

    private static string DetectLang(string text)
    {
        foreach (var ch in text)
            if (ch is >= '\u0600' and <= '\u06FF') return "ar";
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

    private static string? Truncate(string? text, int maxLength)
        => text?.Length > maxLength ? text[..maxLength] + "..." : text;
}

// --- DTOs ---

public sealed record ChatTestRequest(List<TestQuestion>? Questions, string? UserToken = null);

public sealed record TestFeesRequest(string? UserToken = null, int FType = 1);

public sealed record TestQuestion(
    string Message,
    string Lang,
    string ExpectedRoute,
    string Category
);

public sealed record ChatTestResult(
    ChatTestSummary Summary,
    List<TestQuestionResult> Results
);

public sealed class ChatTestSummary
{
    public int TotalQuestions { get; set; }
    public int RoutingCorrect { get; set; }
    public int RoutingWrong { get; set; }
    public Dictionary<string, int> RouteDistribution { get; set; } = new();
    public long TotalTimeMs { get; set; }
    public long AverageTimeMs { get; set; }
    public List<TestError> Errors { get; set; } = [];
}

public sealed record TestError(string Question, string Error);

public sealed class TestQuestionResult
{
    public string Question { get; set; } = "";
    public string? Category { get; set; }
    public string? Lang { get; set; }
    public string? DetectedLang { get; set; }
    public string ExpectedRoute { get; set; } = "";
    public string ActualRoute { get; set; } = "";
    public bool RoutingCorrect { get; set; }
    public decimal Confidence { get; set; }
    public string? FollowUpQuestion { get; set; }
    public string? SelectedFaqId { get; set; }
    public List<string>? SelectedChunkIds { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string? PendingSubmissionSummary { get; set; }
    public ApiCallInfo? ApiCallInfo { get; set; }
    public string? ApiRawResponse { get; set; }
    public string? FinalAnswer { get; set; }
    public string? PlannerRawJson { get; set; }
    public string? Error { get; set; }

    // Retrieval info
    public List<FaqCandidateInfo> FaqCandidates { get; set; } = [];
    public List<ChunkCandidateInfo> DocChunkCandidates { get; set; } = [];

    // Timing
    public long EmbedTimeMs { get; set; }
    public long SearchTimeMs { get; set; }
    public long PlannerTimeMs { get; set; }
    public long AnswerTimeMs { get; set; }
    public long TotalTimeMs { get; set; }
}

public sealed class FaqCandidateInfo
{
    public string? FaqId { get; set; }
    public string? Title { get; set; }
    public string? Question { get; set; }
    public double Score { get; set; }
    public string? Language { get; set; }
}

public sealed class ChunkCandidateInfo
{
    public string? ChunkId { get; set; }
    public string? Filename { get; set; }
    public string? TextPreview { get; set; }
    public double Score { get; set; }
    public string? Source { get; set; }
}

public sealed class ApiCallInfo
{
    public string? ApiId { get; set; }
    public string? ApiName { get; set; }
    public PlannerApiParams? Params { get; set; }
}
