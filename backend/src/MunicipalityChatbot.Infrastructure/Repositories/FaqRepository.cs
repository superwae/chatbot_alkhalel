using Microsoft.EntityFrameworkCore;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Domain.Entities;
using MunicipalityChatbot.Infrastructure.Db;

namespace MunicipalityChatbot.Infrastructure.Repositories;

public sealed class FaqRepository(AppDbContext db) : IFaqRepository
{
    public async Task<Faq?> GetByIdAsync(Guid faqId, CancellationToken ct)
    {
        return await db.Faqs.AsNoTracking().SingleOrDefaultAsync(x => x.FaqId == faqId, ct);
    }

    public async Task<IReadOnlyList<Faq>> SearchActiveAsync(string language, int limit, CancellationToken ct)
    {
        language = (language ?? "EN").Trim().ToUpperInvariant();
        return await db.Faqs.AsNoTracking()
            .Where(x => x.IsActive && x.Language == language)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(Faq faq, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await db.Faqs.SingleOrDefaultAsync(x => x.FaqId == faq.FaqId, ct);

        if (existing is null)
        {
            faq.CreatedAt = now;
            faq.UpdatedAt = now;
            db.Faqs.Add(faq);
        }
        else
        {
            existing.Title = faq.Title;
            existing.Question = faq.Question;
            existing.ShortDescription = faq.ShortDescription;
            existing.Answer = faq.Answer;
            existing.Language = faq.Language;
            existing.TagsCsv = faq.TagsCsv;
            existing.Department = faq.Department;
            existing.IsActive = faq.IsActive;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid faqId, CancellationToken ct)
    {
        var existing = await db.Faqs.SingleOrDefaultAsync(x => x.FaqId == faqId, ct);
        if (existing is null) return;
        db.Faqs.Remove(existing);
        await db.SaveChangesAsync(ct);
    }
}

