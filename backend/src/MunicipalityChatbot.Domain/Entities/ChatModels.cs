namespace MunicipalityChatbot.Domain.Entities;

public sealed class ChatSession
{
    public Guid SessionId { get; set; }
    public string Channel { get; set; } = "web"; // web|widget|other
    public string? WidgetOrigin { get; set; }
    public string? UserLang { get; set; } // ar|en
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ChatMessage
{
    public Guid MessageId { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = "user"; // user|assistant|system
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RoutingDecision
{
    public Guid DecisionId { get; set; }
    public Guid SessionId { get; set; }
    public Guid MessageId { get; set; }
    public string Route { get; set; } = "GENERAL";
    public decimal Confidence { get; set; }
    public Guid? SelectedFaqId { get; set; }
    public string? SelectedChunkIdsCsv { get; set; }
    public Guid? SelectedApiId { get; set; }
    public string PlannerJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ApiCallAudit
{
    public Guid ApiCallId { get; set; }
    public Guid SessionId { get; set; }
    public Guid MessageId { get; set; }
    public Guid ApiId { get; set; }
    public string RequestSummaryJson { get; set; } = "{}"; // never secrets
    public int? ResponseStatusCode { get; set; }
    public string ResponseSummaryJson { get; set; } = "{}";
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

