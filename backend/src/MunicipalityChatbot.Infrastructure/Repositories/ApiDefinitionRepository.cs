using Microsoft.EntityFrameworkCore;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Domain.Entities;
using MunicipalityChatbot.Infrastructure.Db;

namespace MunicipalityChatbot.Infrastructure.Repositories;

public sealed class ApiDefinitionRepository(AppDbContext db) : IApiDefinitionRepository
{
    public async Task<ApiDefinition?> GetByIdAsync(Guid apiId, CancellationToken ct)
    {
        return await db.ApiDefinitions.AsNoTracking().SingleOrDefaultAsync(x => x.ApiId == apiId, ct);
    }

    public async Task<IReadOnlyList<ApiDefinition>> ListAllAsync(CancellationToken ct)
    {
        return await db.ApiDefinitions.AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApiDefinition>> ListAllowedInChatAsync(CancellationToken ct)
    {
        return await db.ApiDefinitions.AsNoTracking()
            .Where(x => x.AllowInChat)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(ApiDefinition api, CancellationToken ct)
    {
        api.ApiId = api.ApiId == Guid.Empty ? Guid.NewGuid() : api.ApiId;
        var now = DateTimeOffset.UtcNow;

        var existing = await db.ApiDefinitions.SingleOrDefaultAsync(x => x.ApiId == api.ApiId, ct);
        if (existing is null)
        {
            api.CreatedAt = now;
            api.UpdatedAt = now;
            db.ApiDefinitions.Add(api);
        }
        else
        {
            existing.ApiName = api.ApiName;
            existing.Description = api.Description;
            existing.BaseUrl = api.BaseUrl;
            existing.Method = api.Method;
            existing.PathTemplate = api.PathTemplate;
            existing.AuthType = api.AuthType;
            existing.AuthConfigJson = api.AuthConfigJson;
            existing.HeadersTemplateJson = api.HeadersTemplateJson;
            existing.QueryParamsSchemaJson = api.QueryParamsSchemaJson;
            existing.BodySchemaJson = api.BodySchemaJson;
            existing.BodyTemplateJson = api.BodyTemplateJson;
            existing.ResponseHandlingNotes = api.ResponseHandlingNotes;
            existing.AllowInChat = api.AllowInChat;
            existing.AllowlistedDomain = api.AllowlistedDomain;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid apiId, CancellationToken ct)
    {
        var existing = await db.ApiDefinitions.SingleOrDefaultAsync(x => x.ApiId == apiId, ct);
        if (existing is null) return;
        db.ApiDefinitions.Remove(existing);
        await db.SaveChangesAsync(ct);
    }
}

