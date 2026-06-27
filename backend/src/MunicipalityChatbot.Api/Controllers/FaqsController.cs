using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Domain.Entities;

namespace MunicipalityChatbot.Api.Controllers;

[ApiController]
[Route("api/faqs")]
public sealed class FaqsController(
    IFaqRepository repo,
    IEmbeddingService embeddings,
    IQdrantService qdrant
) : ControllerBase
{
    public sealed record UpsertFaqRequest(
        Guid? FaqId,
        string Title,
        string Question,
        string ShortDescription,
        string Answer,
        string Language, // AR|EN
        string Tags,
        string Department,
        bool IsActive
    );

    [HttpGet("{faqId:guid}")]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin},{EmployeeRoles.EmployeeEditor}")]
    public async Task<ActionResult<Faq>> Get(Guid faqId, CancellationToken ct)
    {
        var faq = await repo.GetByIdAsync(faqId, ct);
        return faq is null ? NotFound() : Ok(faq);
    }

    [HttpGet("active")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<Faq>>> ListActive([FromQuery] string language = "EN", CancellationToken ct = default)
    {
        var rows = await repo.SearchActiveAsync(language.ToUpperInvariant(), 200, ct);
        return Ok(rows);
    }

    [HttpPost]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin},{EmployeeRoles.EmployeeEditor}")]
    public async Task<ActionResult> Upsert([FromBody] UpsertFaqRequest req, CancellationToken ct)
    {
        var lang = (req.Language ?? "EN").Trim().ToUpperInvariant();
        if (lang is not ("EN" or "AR")) return BadRequest("Language must be EN or AR.");

        var faq = new Faq
        {
            FaqId = req.FaqId ?? Guid.NewGuid(),
            Title = req.Title?.Trim() ?? "",
            Question = req.Question?.Trim() ?? "",
            ShortDescription = req.ShortDescription?.Trim() ?? "",
            Answer = req.Answer?.Trim() ?? "",
            Language = lang,
            TagsCsv = req.Tags?.Trim() ?? "",
            Department = req.Department?.Trim() ?? "",
            IsActive = req.IsActive
        };

        if (string.IsNullOrWhiteSpace(faq.Title) || string.IsNullOrWhiteSpace(faq.Question) || string.IsNullOrWhiteSpace(faq.Answer))
            return BadRequest("Title, Question, and Answer are required.");

        await repo.UpsertAsync(faq, ct);

        // Embed + upsert into Qdrant
        var embedText = $"{faq.Title}\n{faq.Question}\n{faq.ShortDescription}\n{faq.Answer}";
        var vec = await embeddings.EmbedAsync(embedText, ct);
        await qdrant.UpsertFaqAsync(faq, vec, ct);

        return Ok(new { faqId = faq.FaqId });
    }

    [HttpDelete("{faqId:guid}")]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin}")]
    public async Task<ActionResult> Delete(Guid faqId, CancellationToken ct)
    {
        // Delete from Qdrant
        await qdrant.DeleteFaqAsync(faqId, ct);

        // Delete from PostgreSQL
        await repo.DeleteAsync(faqId, ct);

        return NoContent();
    }
}

