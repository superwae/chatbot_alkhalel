namespace MunicipalityChatbot.Domain.Entities;

public sealed class Document
{
    public Guid DocId { get; set; }
    public string Filename { get; set; } = "";
    public string FileType { get; set; } = ""; // pdf|docx|xlsx|png|jpg|jpeg
    public long FileSizeBytes { get; set; }
    public string? DetectedLanguage { get; set; } // EN|AR|null
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class DocumentChunk
{
    public Guid ChunkId { get; set; }
    public Guid DocId { get; set; }
    public string Filename { get; set; } = "";
    public string FileType { get; set; } = "";
    public string? Language { get; set; } // EN|AR|null
    public int? PageNumber { get; set; }
    public string? SheetName { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

