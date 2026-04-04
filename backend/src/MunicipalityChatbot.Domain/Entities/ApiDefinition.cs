namespace MunicipalityChatbot.Domain.Entities;

public sealed class ApiDefinition
{
    public Guid ApiId { get; set; }
    public string ApiName { get; set; } = "";
    public string Description { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string PathTemplate { get; set; } = "";
    public string AuthType { get; set; } = "None"; // None|ApiKey|BearerToken|Basic
    public string AuthConfigJson { get; set; } = "{}"; // references to env var names only
    public string HeadersTemplateJson { get; set; } = "{}";
    public string QueryParamsSchemaJson { get; set; } = "{}";
    public string BodySchemaJson { get; set; } = "{}";
    public string? BodyTemplateJson { get; set; }
    public string ResponseHandlingNotes { get; set; } = "";
    public bool AllowInChat { get; set; }
    public string AllowlistedDomain { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

