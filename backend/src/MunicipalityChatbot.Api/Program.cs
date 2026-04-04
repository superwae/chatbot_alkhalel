using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using MunicipalityChatbot.Api;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Infrastructure.Config;
using MunicipalityChatbot.Infrastructure.Repositories;
using MunicipalityChatbot.Infrastructure.Security;
using MunicipalityChatbot.Infrastructure.Services;
using MunicipalityChatbot.Infrastructure.Db;
using MunicipalityChatbot.Api.BackgroundServices;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddEnvironmentVariables();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Swashbuckle will throw for [FromForm] IFormFile unless specially configured.
    // To keep Swagger working, exclude file-upload actions from the OpenAPI doc.
    c.DocInclusionPredicate((_, apiDesc) =>
    {
        if (apiDesc.ParameterDescriptions.Any(p => p.Type == typeof(IFormFile)))
            return false;

        // Extra safety: if we detect the documents upload action, exclude it.
        if (apiDesc.ActionDescriptor is ControllerActionDescriptor cad &&
            string.Equals(cad.ControllerName, "Documents", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(cad.ActionName, "Upload", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    });
});

// Options
var llm = builder.Configuration.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions();
// Env var `LLM__PROVIDER` maps to configuration key `LLM:PROVIDER`
llm.Provider = builder.Configuration["LLM:PROVIDER"] ?? llm.Provider;
builder.Services.AddSingleton(llm);

var openAi = builder.Configuration.GetSection("OpenAi").Get<OpenAiOptions>() ?? new OpenAiOptions();
// Env vars like `OPENAI__API_KEY` map to keys like `OPENAI:API_KEY`
openAi.ApiKey = builder.Configuration["OPENAI:API_KEY"] ?? openAi.ApiKey;
openAi.BaseUrl = builder.Configuration["OPENAI:BASE_URL"] ?? openAi.BaseUrl;
openAi.Model = builder.Configuration["OPENAI:MODEL"] ?? openAi.Model;
openAi.PlannerModel = builder.Configuration["OPENAI:PLANNER_MODEL"] ?? openAi.PlannerModel;
openAi.EmbeddingModel = builder.Configuration["OPENAI:EMBEDDING_MODEL"] ?? openAi.EmbeddingModel;
builder.Services.AddSingleton(openAi);

var gemini = builder.Configuration.GetSection("Gemini").Get<GeminiOptions>() ?? new GeminiOptions();
gemini.ApiKey = builder.Configuration["GEMINI:API_KEY"] ?? gemini.ApiKey;
gemini.BaseUrl = builder.Configuration["GEMINI:BASE_URL"] ?? gemini.BaseUrl;
gemini.Model = builder.Configuration["GEMINI:MODEL"] ?? gemini.Model;
gemini.PlannerModel = builder.Configuration["GEMINI:PLANNER_MODEL"] ?? gemini.PlannerModel;
gemini.EmbeddingModel = builder.Configuration["GEMINI:EMBEDDING_MODEL"] ?? gemini.EmbeddingModel;
builder.Services.AddSingleton(gemini);

var qdrant = builder.Configuration.GetSection("Qdrant").Get<QdrantOptions>() ?? new QdrantOptions();
qdrant.Url = builder.Configuration["QDRANT:URL"] ?? qdrant.Url;
qdrant.ApiKey = builder.Configuration["QDRANT:API_KEY"] ?? qdrant.ApiKey;
qdrant.Collection = builder.Configuration["QDRANT:COLLECTION"] ?? qdrant.Collection;
if (int.TryParse(builder.Configuration["QDRANT:VECTOR_SIZE"], out var vs)) qdrant.VectorSize = vs;
builder.Services.AddSingleton(qdrant);
builder.Services.AddSingleton(builder.Configuration.GetSection("Auth:Jwt").Get<JwtOptions>() ?? new JwtOptions());

var cors = builder.Configuration.GetSection("Cors").Get<CorsOptions>() ?? new CorsOptions();
cors.AllowedOrigins = builder.Configuration["CORS:ALLOWED_ORIGINS"] ?? cors.AllowedOrigins;
cors.WidgetAllowedOrigins = builder.Configuration["CORS:WIDGET_ALLOWED_ORIGINS"] ?? cors.WidgetAllowedOrigins;
builder.Services.AddSingleton(cors);

var widget = builder.Configuration.GetSection("Widget").Get<WidgetOptions>() ?? new WidgetOptions();
widget.ApiKey = builder.Configuration["WIDGET:API_KEY"] ?? widget.ApiKey;
builder.Services.AddSingleton(widget);
builder.Services.AddSingleton(builder.Configuration.GetSection("RateLimit:PublicChat").Get<PublicChatRateLimitOptions>() ?? new PublicChatRateLimitOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("ApiCalls").Get<ApiCallsOptions>() ?? new ApiCallsOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Postgres").Get<PostgresOptions>() ?? new PostgresOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Database").Get<DatabaseInitOptions>() ?? new DatabaseInitOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Seed:Admin").Get<AdminSeedOptions>() ?? new AdminSeedOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Seed:Faq").Get<FaqSeedOptions>() ?? new FaqSeedOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Seed:Api").Get<ApiSeedOptions>() ?? new ApiSeedOptions());

var websiteCrawl = builder.Configuration.GetSection("WebsiteCrawl").Get<WebsiteCrawlOptions>() ?? new WebsiteCrawlOptions();
websiteCrawl.Enabled = builder.Configuration["WEBSITE_CRAWL:ENABLED"]?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? websiteCrawl.Enabled;
websiteCrawl.Url = builder.Configuration["WEBSITE_CRAWL:URL"] ?? websiteCrawl.Url;
if (int.TryParse(builder.Configuration["WEBSITE_CRAWL:INTERVAL_HOURS"], out var intervalHours)) websiteCrawl.IntervalHours = intervalHours;
builder.Services.AddSingleton(websiteCrawl);

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        var corsOptions = builder.Configuration.GetSection("Cors").Get<CorsOptions>() ?? new CorsOptions();
        var allowed = (corsOptions.AllowedOrigins ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var widgetAllowed = (corsOptions.WidgetAllowedOrigins ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var combined = allowed.Concat(widgetAllowed).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // Check if wildcard is configured - use SetIsOriginAllowed for flexibility
        if (combined.Contains("*"))
        {
            // Allow any origin including "null" (from file:// URLs)
            policy.SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(combined.Length == 0 ? ["http://localhost:5173"] : combined)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });

    // Widget policy - allows any origin for chat endpoints
    options.AddPolicy("Widget", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Auth (employees only)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection("Auth:Jwt").Get<JwtOptions>() ?? new JwtOptions();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey))
        };
    });
builder.Services.AddAuthorization();

// Rate limiting for public chat
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("PublicChat", context =>
    {
        var cfg = builder.Configuration.GetSection("RateLimit:PublicChat").Get<PublicChatRateLimitOptions>() ?? new PublicChatRateLimitOptions();
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = cfg.PermitLimit,
                Window = TimeSpan.FromSeconds(cfg.WindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

// HttpClients
builder.Services.AddHttpClient<OpenAiClient>();
builder.Services.AddHttpClient<GeminiClient>();
builder.Services.AddHttpClient<VisionOcrService>();
builder.Services.AddScoped<IOcrService, VisionOcrService>();
builder.Services.AddHttpClient<QdrantClient>();
builder.Services.AddHttpClient<ApiHttpClient>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // External municipality APIs (e.g. egate.hebron-city.ps) may use certificates
        // not trusted by the Docker container's CA store. Bypass SSL validation for API calls.
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

// DB + repos
// Prefer explicit env var for deployments/compose, fall back to appsettings.json connection string.
// Env var `POSTGRES__CONNECTION_STRING` maps to `POSTGRES:CONNECTION_STRING`
var pgConn = builder.Configuration["POSTGRES:CONNECTION_STRING"]
             ?? builder.Configuration.GetConnectionString("DefaultConnection")
             ?? "";

builder.Services.AddDbContext<AppDbContext>(opt =>
{
    opt.UseNpgsql(pgConn);
});

builder.Services.AddScoped<IFaqRepository, FaqRepository>();
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IApiDefinitionRepository, ApiDefinitionRepository>();
builder.Services.AddScoped<IEmployeeRepository, EmployeeRepository>();
builder.Services.AddScoped<IChatAuditRepository, ChatAuditRepository>();
builder.Services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();

// Security services
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService>(sp => new JwtTokenService(sp.GetRequiredService<JwtOptions>()));

// AI/Qdrant services
builder.Services.AddScoped<IEmbeddingService>(sp =>
    string.Equals(sp.GetRequiredService<LlmOptions>().Provider, "Gemini", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<GeminiClient>()
        : sp.GetRequiredService<OpenAiClient>());
builder.Services.AddScoped<IPlanningService>(sp =>
    string.Equals(sp.GetRequiredService<LlmOptions>().Provider, "Gemini", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<GeminiClient>()
        : sp.GetRequiredService<OpenAiClient>());
builder.Services.AddScoped<IRagAnswerService>(sp =>
    string.Equals(sp.GetRequiredService<LlmOptions>().Provider, "Gemini", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<GeminiClient>()
        : sp.GetRequiredService<OpenAiClient>());
builder.Services.AddScoped<IApiAnswerService>(sp =>
    string.Equals(sp.GetRequiredService<LlmOptions>().Provider, "Gemini", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<GeminiClient>()
        : sp.GetRequiredService<OpenAiClient>());
builder.Services.AddScoped<IGeneralAnswerService>(sp =>
    string.Equals(sp.GetRequiredService<LlmOptions>().Provider, "Gemini", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<GeminiClient>()
        : sp.GetRequiredService<OpenAiClient>());
builder.Services.AddScoped<IQdrantService, QdrantService>();

// Orchestration services
builder.Services.AddScoped<IApiExecutionService, ApiExecutionService>();
builder.Services.AddScoped<IIngestionService, IngestionService>();
builder.Services.AddScoped<IWebsiteCrawlerService, WebsiteCrawlerService>();
builder.Services.AddScoped<ChatOrchestrator>();

// HttpClient for website crawler
builder.Services.AddHttpClient<WebsiteCrawlerService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MunicipalityChatbot/1.0");
});

// Background service for scheduled website crawling
builder.Services.AddHostedService<WebsiteCrawlBackgroundService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Serve static files (widget JS)
app.UseStaticFiles();

app.UseCors("Default");
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// DB init + optional admin seed
using (var scope = app.Services.CreateScope())
{
    await DatabaseInitializer.InitializeAsync(
        scope.ServiceProvider,
        app.Logger,
        app.Environment.IsDevelopment(),
        CancellationToken.None);

    // Ensure Qdrant collection exists
    var qdrantService = scope.ServiceProvider.GetRequiredService<IQdrantService>();
    var qdrantOptions = scope.ServiceProvider.GetRequiredService<QdrantOptions>();
    app.Logger.LogInformation("Ensuring Qdrant collection '{Collection}' exists...", qdrantOptions.Collection);
    await qdrantService.EnsureCollectionAsync(qdrantOptions.VectorSize, CancellationToken.None);
    app.Logger.LogInformation("Qdrant collection '{Collection}' is ready", qdrantOptions.Collection);
}

await app.RunAsync();

