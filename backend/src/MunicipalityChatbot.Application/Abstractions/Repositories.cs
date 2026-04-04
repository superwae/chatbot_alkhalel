using MunicipalityChatbot.Domain.Entities;

namespace MunicipalityChatbot.Application.Abstractions;

public interface IFaqRepository
{
    Task<Faq?> GetByIdAsync(Guid faqId, CancellationToken ct);
    Task<IReadOnlyList<Faq>> SearchActiveAsync(string language, int limit, CancellationToken ct);
    Task UpsertAsync(Faq faq, CancellationToken ct);
    Task DeleteAsync(Guid faqId, CancellationToken ct);
}

public interface IDocumentRepository
{
    Task<Document?> GetDocAsync(Guid docId, CancellationToken ct);
    Task<IReadOnlyList<Document>> FindByFilenameAsync(string filename, CancellationToken ct);
    Task<Guid> CreateDocAsync(Document doc, CancellationToken ct);
    Task<IReadOnlyList<Document>> ListDocsAsync(int limit, CancellationToken ct);
    Task AddChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct);
    Task<IReadOnlyList<DocumentChunk>> GetChunksByIdsAsync(IEnumerable<Guid> chunkIds, CancellationToken ct);
    Task<IReadOnlyList<DocumentChunk>> ListAllChunksAsync(CancellationToken ct);
    Task DeleteDocAsync(Guid docId, CancellationToken ct);
}

public interface IApiDefinitionRepository
{
    Task<ApiDefinition?> GetByIdAsync(Guid apiId, CancellationToken ct);
    Task<IReadOnlyList<ApiDefinition>> ListAllAsync(CancellationToken ct);
    Task<IReadOnlyList<ApiDefinition>> ListAllowedInChatAsync(CancellationToken ct);
    Task UpsertAsync(ApiDefinition api, CancellationToken ct);
    Task DeleteAsync(Guid apiId, CancellationToken ct);
}

public interface IEmployeeRepository
{
    Task<EmployeeUser?> GetByUsernameAsync(string username, CancellationToken ct);
}

public interface IChatAuditRepository
{
    Task<Guid> CreateSessionAsync(ChatSession session, CancellationToken ct);
    Task AddMessageAsync(ChatMessage message, CancellationToken ct);
    Task AddRoutingDecisionAsync(RoutingDecision decision, CancellationToken ct);
    Task AddApiCallAsync(ApiCallAudit audit, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(Guid sessionId, int limit, CancellationToken ct);
}

public interface IAnalyticsRepository
{
    Task<object> GetSummaryAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
    Task<object> GetChatHistoryAsync(int limit, int offset, DateTimeOffset? from, DateTimeOffset? to, string? routeFilter, CancellationToken ct);
    Task<object> GetConversationAsync(Guid sessionId, CancellationToken ct);
    Task<object> ExportChatLogsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
}

