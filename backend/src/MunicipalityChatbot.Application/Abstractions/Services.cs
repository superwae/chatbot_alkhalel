using MunicipalityChatbot.Application.Models;
using MunicipalityChatbot.Domain.Entities;

namespace MunicipalityChatbot.Application.Abstractions;

public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
}

public interface IQdrantService
{
    Task EnsureCollectionAsync(int vectorSize, CancellationToken ct);
    Task UpsertFaqAsync(Faq faq, float[] vector, CancellationToken ct);
    Task UpsertDocChunkAsync(DocumentChunk chunk, float[] vector, CancellationToken ct);
    Task UpsertWebsiteChunkAsync(WebsiteChunk chunk, float[] vector, CancellationToken ct);
    Task<IReadOnlyList<QdrantPoint>> SearchAsync(string type, float[] vector, int topK, string? language, CancellationToken ct);
    Task DeleteDocumentAsync(Guid docId, CancellationToken ct);
    Task DeleteFaqAsync(Guid faqId, CancellationToken ct);
    Task DeleteWebsiteChunksAsync(Guid pageId, CancellationToken ct);
    Task DeleteAllWebsiteChunksAsync(CancellationToken ct);
}

public interface IPlanningService
{
    Task<PlannerResult> PlanAsync(PlannerInput input, CancellationToken ct);
}

public interface IRagAnswerService
{
    Task<string> AnswerFromChunksAsync(string userMessage, string userLang, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct);
    IAsyncEnumerable<string> StreamAnswerFromChunksAsync(string userMessage, string userLang, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct);
}

public interface IApiAnswerService
{
    Task<string> AnswerFromApiResultAsync(string userMessage, string userLang, string apiName, string apiResultJson, string notes, CancellationToken ct);
}

public interface IGeneralAnswerService
{
    Task<string> AnswerGeneralAsync(string userMessage, string userLang, CancellationToken ct);
    IAsyncEnumerable<string> StreamAnswerGeneralAsync(string userMessage, string userLang, CancellationToken ct);
}

public interface IApiExecutionService
{
    Task<ApiExecutionResult> ExecuteAsync(ApiDefinition api, PlannerApiCall apiCall, CancellationToken ct, string? userToken = null);
}

public interface IOcrService
{
    Task<string> ExtractTextFromImageAsync(byte[] imageBytes, CancellationToken ct);
}

public interface IIngestionService
{
    Task IngestDocumentAsync(Guid docId, string filename, string fileType, Stream fileStream, CancellationToken ct);
}

public interface IWebsiteCrawlerService
{
    Task<WebsiteCrawlResult> CrawlWebsiteAsync(string baseUrl, CancellationToken ct);
}

public record WebsiteChunk(
    Guid ChunkId,
    Guid PageId,
    string Url,
    string Title,
    int ChunkIndex,
    string Text
);

public record WebsiteCrawlResult(
    int PagesProcessed,
    int PagesSkipped,
    int ChunksCreated,
    List<string> Errors
);

