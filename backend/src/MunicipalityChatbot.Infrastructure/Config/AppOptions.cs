namespace MunicipalityChatbot.Infrastructure.Config;

public sealed class DatabaseInitOptions
{
    /// <summary>
    /// When true, the API will run EF Core migrations on startup.
    /// Recommended for dev/compose; for production you may prefer running migrations separately.
    /// </summary>
    public bool AutoMigrate { get; set; } = false;
}

public sealed class AdminSeedOptions
{
    /// <summary>
    /// When true, the API will ensure an initial admin employee exists.
    /// Seeding only occurs when <see cref="Password"/> is provided (non-empty).
    /// </summary>
    public bool Enabled { get; set; } = true;

    public string Username { get; set; } = "admin";

    /// <summary>
    /// Plaintext initial password (recommended to provide via environment variable).
    /// </summary>
    public string Password { get; set; } = "";

    public string Role { get; set; } = "EmployeeAdmin";
}

public sealed class LlmOptions
{
    /// <summary>
    /// Supported: "OpenAI" | "Gemini"
    /// </summary>
    public string Provider { get; set; } = "OpenAI";
}

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o";
    public string PlannerModel { get; set; } = "gpt-4o";
    public string EmbeddingModel { get; set; } = "text-embedding-3-large";
    public string VisionModel { get; set; } = "gpt-5.2";
}

public sealed class GeminiOptions
{
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Google Generative Language API base URL (v1beta).
    /// Example: https://generativelanguage.googleapis.com/v1beta
    /// </summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>
    /// Example: gemini-2.0-flash
    /// </summary>
    public string Model { get; set; } = "gemini-2.0-flash";

    /// <summary>
    /// Stronger model for the planner/routing. Falls back to Model if not set.
    /// </summary>
    public string PlannerModel { get; set; } = "";

    /// <summary>
    /// Example: text-embedding-004
    /// </summary>
    public string EmbeddingModel { get; set; } = "text-embedding-004";
}

public sealed class QdrantOptions
{
    public string Url { get; set; } = "http://localhost:6333";
    public string? ApiKey { get; set; }
    public string Collection { get; set; } = "municipality_knowledge";
    public int VectorSize { get; set; } = 3072;
}

public sealed class PostgresOptions
{
    public string ConnectionString { get; set; } = "";
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "municipality-chatbot";
    public string Audience { get; set; } = "municipality-chatbot";
    public string SigningKey { get; set; } = "PLEASE_CHANGE_TO_A_LONG_RANDOM_SECRET";
    public int AccessTokenMinutes { get; set; } = 60;
}

public sealed class CorsOptions
{
    public string AllowedOrigins { get; set; } = "";
    public string WidgetAllowedOrigins { get; set; } = "";
}

public sealed class WidgetOptions
{
    public string? ApiKey { get; set; }
}

public sealed class PublicChatRateLimitOptions
{
    public int PermitLimit { get; set; } = 30;
    public int WindowSeconds { get; set; } = 60;
}

public sealed class FaqSeedOptions
{
    /// <summary>
    /// When true, the API will seed FAQs from the embedded JSON file on startup.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to external FAQ seed JSON file. If empty, uses embedded default.
    /// </summary>
    public string ExternalFilePath { get; set; } = "";
}

public sealed class ApiSeedOptions
{
    /// <summary>
    /// When true, the API will seed API definitions from the embedded JSON file on startup.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to external API seed JSON file. If empty, uses embedded default.
    /// </summary>
    public string ExternalFilePath { get; set; } = "";
}

public sealed class ApiCallsOptions
{
    public int TimeoutSeconds { get; set; } = 1800;
    public int MaxRetries { get; set; } = 1;
}

public sealed class WebsiteCrawlOptions
{
    /// <summary>
    /// When true, enables automatic website crawling on a schedule.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The URL to crawl (default: hebron-city.ps).
    /// </summary>
    public string Url { get; set; } = "https://www.hebron-city.ps";

    /// <summary>
    /// Interval between crawls in hours (default: 168 = weekly).
    /// </summary>
    public int IntervalHours { get; set; } = 168;
}

