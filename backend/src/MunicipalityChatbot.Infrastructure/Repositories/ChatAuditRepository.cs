using Microsoft.EntityFrameworkCore;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Domain.Entities;
using MunicipalityChatbot.Infrastructure.Db;

namespace MunicipalityChatbot.Infrastructure.Repositories;

public sealed class ChatAuditRepository(AppDbContext db) : IChatAuditRepository
{
    public async Task<Guid> CreateSessionAsync(ChatSession session, CancellationToken ct)
    {
        session.SessionId = session.SessionId == Guid.Empty ? Guid.NewGuid() : session.SessionId;
        session.CreatedAt = DateTimeOffset.UtcNow;
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session.SessionId;
    }

    public async Task AddMessageAsync(ChatMessage message, CancellationToken ct)
    {
        message.MessageId = message.MessageId == Guid.Empty ? Guid.NewGuid() : message.MessageId;
        message.CreatedAt = DateTimeOffset.UtcNow;
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddRoutingDecisionAsync(RoutingDecision decision, CancellationToken ct)
    {
        decision.DecisionId = decision.DecisionId == Guid.Empty ? Guid.NewGuid() : decision.DecisionId;
        decision.CreatedAt = DateTimeOffset.UtcNow;
        db.RoutingDecisions.Add(decision);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddApiCallAsync(ApiCallAudit audit, CancellationToken ct)
    {
        audit.ApiCallId = audit.ApiCallId == Guid.Empty ? Guid.NewGuid() : audit.ApiCallId;
        audit.CreatedAt = DateTimeOffset.UtcNow;
        db.ApiCalls.Add(audit);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(Guid sessionId, int limit, CancellationToken ct)
    {
        var messages = await db.ChatMessages.AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        // Return in chronological order (oldest first)
        messages.Reverse();
        return messages;
    }
}

