namespace MunicipalityChatbot.Domain.Entities;

public sealed class CrawledPage
{
    public Guid PageId { get; set; }
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public int ChunkCount { get; set; }
    public DateTimeOffset LastCrawledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
