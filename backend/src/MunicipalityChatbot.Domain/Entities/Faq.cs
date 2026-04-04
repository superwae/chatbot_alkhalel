namespace MunicipalityChatbot.Domain.Entities;

public sealed class Faq
{
    public Guid FaqId { get; set; }
    public string Title { get; set; } = "";
    public string Question { get; set; } = "";
    public string ShortDescription { get; set; } = "";
    public string Answer { get; set; } = "";
    public string Language { get; set; } = "EN"; // EN|AR
    public string TagsCsv { get; set; } = "";
    public string Department { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

