using System.Text.Json.Serialization;

namespace MunicipalityChatbot.Application.Models;

public sealed record PublicChatRequest(
    string Message,
    string? Lang,
    Guid? SessionId,
    string? UserToken = null,
    string? CustomerId = null
);

public sealed record PublicChatResponse(
    Guid SessionId,
    string Route,
    string Answer,
    IReadOnlyList<Citation> Citations,
    string? FollowUpQuestion
);

public sealed record Citation(string Label, string? ChunkId);

public sealed record QdrantPoint(
    string PointId,
    double Score,
    Dictionary<string, object> Payload
);

public sealed class PlannerInput
{
    public required string UserMessage { get; init; }
    public required string UserLang { get; init; } // ar|en
    public required IReadOnlyList<object> FaqCandidates { get; init; }
    public required IReadOnlyList<object> DocChunkCandidates { get; init; }
    public required IReadOnlyList<object> ApiDefinitions { get; init; }
    public required object SessionState { get; init; }
    public IReadOnlyList<ConversationMessage>? ConversationHistory { get; init; }
}

public sealed record ConversationMessage(string Role, string Text);

public sealed class PlannerResult
{
    [JsonPropertyName("route")]
    public string Route { get; set; } = "GENERAL";

    [JsonPropertyName("confidence")]
    public decimal Confidence { get; set; }

    [JsonPropertyName("selectedFaqId")]
    public string? SelectedFaqId { get; set; }

    [JsonPropertyName("selectedChunkIds")]
    public List<string>? SelectedChunkIds { get; set; }

    [JsonPropertyName("apiCall")]
    public PlannerApiCall? ApiCall { get; set; }

    [JsonPropertyName("followUpQuestion")]
    public string? FollowUpQuestion { get; set; }

    [JsonPropertyName("finalAnswerStyle")]
    public string FinalAnswerStyle { get; set; } = "short, clear, municipality-friendly";

    /// <summary>
    /// For POST APIs: true means we need user confirmation before executing.
    /// </summary>
    [JsonPropertyName("requiresConfirmation")]
    public bool RequiresConfirmation { get; set; }

    /// <summary>
    /// For POST APIs awaiting confirmation: a formatted summary of what will be submitted.
    /// </summary>
    [JsonPropertyName("pendingSubmissionSummary")]
    public string? PendingSubmissionSummary { get; set; }

    /// <summary>
    /// True when user has confirmed the submission in this message.
    /// </summary>
    [JsonPropertyName("userConfirmed")]
    public bool UserConfirmed { get; set; }

    public string RawJson { get; set; } = "{}";
}

public sealed class PlannerApiCall
{
    [JsonPropertyName("apiId")]
    public string? ApiId { get; set; }

    [JsonPropertyName("params")]
    public PlannerApiParams Params { get; set; } = new();
}

public sealed class PlannerApiParams
{
    [JsonPropertyName("query")]
    public Dictionary<string, object>? Query { get; set; }

    [JsonPropertyName("path")]
    public Dictionary<string, object>? Path { get; set; }

    [JsonPropertyName("body")]
    public Dictionary<string, object>? Body { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, object>? Headers { get; set; }
}

public sealed record ApiExecutionResult(
    bool Success,
    int? StatusCode,
    string ResponseBody,
    string? Error
);

// Streaming events for SSE
public sealed record StreamEvent(
    string Type, // "stage" | "meta" | "chunk" | "error"
    Guid? SessionId = null,
    string? Route = null,
    IReadOnlyList<Citation>? Citations = null,
    string? FollowUpQuestion = null,
    string? Content = null, // For "chunk" type - the text fragment
    string? Error = null, // For "error" type
    string? Stage = null // For "stage" type - current processing stage
);

