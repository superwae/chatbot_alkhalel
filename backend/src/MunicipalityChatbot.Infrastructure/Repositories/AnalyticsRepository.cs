using Microsoft.EntityFrameworkCore;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Infrastructure.Db;

namespace MunicipalityChatbot.Infrastructure.Repositories;

public sealed class AnalyticsRepository(AppDbContext db) : IAnalyticsRepository
{
    public async Task<object> GetSummaryAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var decisions = db.RoutingDecisions.AsNoTracking().AsQueryable();
        if (from is not null) decisions = decisions.Where(x => x.CreatedAt >= from);
        if (to is not null) decisions = decisions.Where(x => x.CreatedAt <= to);

        var routeDistribution = await decisions
            .GroupBy(x => x.Route)
            .Select(g => new { route = g.Key, cnt = g.Count() })
            .OrderByDescending(x => x.cnt)
            .ToListAsync(ct);

        var topFaqs = await decisions
            .Where(x => x.Route == "FAQ" && x.SelectedFaqId != null)
            .GroupBy(x => x.SelectedFaqId)
            .Select(g => new { faqId = g.Key, cnt = g.Count() })
            .OrderByDescending(x => x.cnt)
            .Take(10)
            .ToListAsync(ct);

        var calls = db.ApiCalls.AsNoTracking().AsQueryable();
        if (from is not null) calls = calls.Where(x => x.CreatedAt >= from);
        if (to is not null) calls = calls.Where(x => x.CreatedAt <= to);

        var failedApis = await calls
            .Where(x => x.Error != null || x.ResponseStatusCode == null || x.ResponseStatusCode < 200 || x.ResponseStatusCode >= 300)
            .GroupBy(x => x.ApiId)
            .Select(g => new { apiId = g.Key, cnt = g.Count() })
            .OrderByDescending(x => x.cnt)
            .Take(10)
            .ToListAsync(ct);

        var totalSessions = await db.ChatSessions.AsNoTracking()
            .Where(s => (from == null || s.CreatedAt >= from) && (to == null || s.CreatedAt <= to))
            .CountAsync(ct);

        var totalMessages = await db.ChatMessages.AsNoTracking()
            .Where(m => (from == null || m.CreatedAt >= from) && (to == null || m.CreatedAt <= to))
            .CountAsync(ct);

        return new { routeDistribution, topFaqs, failedApis, totalSessions, totalMessages };
    }

    public async Task<object> GetChatHistoryAsync(int limit, int offset, DateTimeOffset? from, DateTimeOffset? to, string? routeFilter, CancellationToken ct)
    {
        // Get routing decisions with filters
        var decisionsQuery = db.RoutingDecisions.AsNoTracking().AsQueryable();
        if (from is not null) decisionsQuery = decisionsQuery.Where(x => x.CreatedAt >= from);
        if (to is not null) decisionsQuery = decisionsQuery.Where(x => x.CreatedAt <= to);
        if (!string.IsNullOrWhiteSpace(routeFilter)) decisionsQuery = decisionsQuery.Where(x => x.Route == routeFilter);

        var totalCount = await decisionsQuery.CountAsync(ct);

        var decisions = await decisionsQuery
            .OrderByDescending(x => x.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(d => new
            {
                d.DecisionId,
                d.SessionId,
                d.MessageId,
                d.Route,
                d.Confidence,
                d.SelectedFaqId,
                d.SelectedApiId,
                d.SelectedChunkIdsCsv,
                d.CreatedAt
            })
            .ToListAsync(ct);

        // Get related user messages
        var messageIds = decisions.Select(d => d.MessageId).ToList();
        var userMessages = await db.ChatMessages.AsNoTracking()
            .Where(m => messageIds.Contains(m.MessageId))
            .Select(m => new { m.MessageId, m.SessionId, m.Text })
            .ToListAsync(ct);

        // Get assistant responses (find messages with same session, role=assistant, created after user message)
        var sessionIds = decisions.Select(d => d.SessionId).Distinct().ToList();
        var assistantMessages = await db.ChatMessages.AsNoTracking()
            .Where(m => sessionIds.Contains(m.SessionId) && m.Role == "assistant")
            .Select(m => new { m.SessionId, m.Text, m.CreatedAt })
            .ToListAsync(ct);

        // Get session info
        var sessions = await db.ChatSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.SessionId))
            .Select(s => new { s.SessionId, s.UserLang, s.Channel })
            .ToListAsync(ct);

        // Get FAQ titles
        var faqIds = decisions.Where(d => d.SelectedFaqId != null).Select(d => d.SelectedFaqId!.Value).Distinct().ToList();
        var faqs = await db.Faqs.AsNoTracking()
            .Where(f => faqIds.Contains(f.FaqId))
            .Select(f => new { f.FaqId, f.Title, f.Question })
            .ToListAsync(ct);

        // Get API names
        var apiIds = decisions.Where(d => d.SelectedApiId != null).Select(d => d.SelectedApiId!.Value).Distinct().ToList();
        var apis = await db.ApiDefinitions.AsNoTracking()
            .Where(a => apiIds.Contains(a.ApiId))
            .Select(a => new { a.ApiId, a.ApiName })
            .ToListAsync(ct);

        // Get chunk info for RAG
        var allChunkIds = decisions
            .Where(d => !string.IsNullOrEmpty(d.SelectedChunkIdsCsv))
            .SelectMany(d => d.SelectedChunkIdsCsv!.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Where(id => Guid.TryParse(id, out _))
            .Select(id => Guid.Parse(id))
            .Distinct()
            .ToList();

        var chunks = await db.DocumentChunks.AsNoTracking()
            .Where(c => allChunkIds.Contains(c.ChunkId))
            .Select(c => new { c.ChunkId, c.Filename, c.ChunkIndex })
            .ToListAsync(ct);

        var result = decisions.Select(d =>
        {
            var session = sessions.FirstOrDefault(s => s.SessionId == d.SessionId);
            var userMsg = userMessages.FirstOrDefault(m => m.MessageId == d.MessageId);
            var faq = d.SelectedFaqId != null ? faqs.FirstOrDefault(f => f.FaqId == d.SelectedFaqId) : null;
            var api = d.SelectedApiId != null ? apis.FirstOrDefault(a => a.ApiId == d.SelectedApiId) : null;

            // Find assistant response for this session after this decision
            var assistantMsg = assistantMessages
                .Where(m => m.SessionId == d.SessionId && m.CreatedAt >= d.CreatedAt)
                .OrderBy(m => m.CreatedAt)
                .FirstOrDefault();

            // Parse chunk IDs and get chunk info
            List<object>? chunkInfo = null;
            if (!string.IsNullOrEmpty(d.SelectedChunkIdsCsv))
            {
                var chunkIds = d.SelectedChunkIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Where(id => Guid.TryParse(id, out _))
                    .Select(id => Guid.Parse(id))
                    .ToList();
                chunkInfo = chunks
                    .Where(c => chunkIds.Contains(c.ChunkId))
                    .Select(c => (object)new { c.ChunkId, c.Filename, c.ChunkIndex })
                    .ToList();
            }

            return new
            {
                d.DecisionId,
                d.SessionId,
                d.CreatedAt,
                d.Route,
                d.Confidence,
                UserLanguage = session?.UserLang,
                Channel = session?.Channel,
                Question = userMsg?.Text,
                Answer = assistantMsg?.Text,
                FaqTitle = faq?.Title,
                FaqQuestion = faq?.Question,
                ApiName = api?.ApiName,
                RagChunks = chunkInfo
            };
        }).ToList();

        return new { logs = result, totalCount };
    }

    public async Task<object> GetConversationAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.ChatSessions.AsNoTracking()
            .Where(s => s.SessionId == sessionId)
            .Select(s => new { s.SessionId, s.UserLang, s.Channel, s.WidgetOrigin, s.CreatedAt })
            .FirstOrDefaultAsync(ct);

        if (session == null) return new { error = "Session not found" };

        var messages = await db.ChatMessages.AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.MessageId, m.Role, m.Text, m.CreatedAt })
            .ToListAsync(ct);

        var decisions = await db.RoutingDecisions.AsNoTracking()
            .Where(d => d.SessionId == sessionId)
            .Select(d => new { d.MessageId, d.Route, d.Confidence, d.SelectedFaqId, d.SelectedApiId, d.SelectedChunkIdsCsv })
            .ToListAsync(ct);

        var apiCalls = await db.ApiCalls.AsNoTracking()
            .Where(a => a.SessionId == sessionId)
            .Select(a => new { a.MessageId, a.ApiId, a.ResponseStatusCode, a.Error })
            .ToListAsync(ct);

        return new { session, messages, decisions, apiCalls };
    }

    public async Task<object> ExportChatLogsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        // Get all routing decisions in range
        var decisionsQuery = db.RoutingDecisions.AsNoTracking().AsQueryable();
        if (from is not null) decisionsQuery = decisionsQuery.Where(x => x.CreatedAt >= from);
        if (to is not null) decisionsQuery = decisionsQuery.Where(x => x.CreatedAt <= to);

        var decisions = await decisionsQuery
            .OrderByDescending(x => x.CreatedAt)
            .Select(d => new
            {
                d.DecisionId,
                d.SessionId,
                d.MessageId,
                d.Route,
                d.Confidence,
                d.SelectedFaqId,
                d.SelectedApiId,
                d.SelectedChunkIdsCsv,
                d.CreatedAt
            })
            .ToListAsync(ct);

        // Get all related data
        var messageIds = decisions.Select(d => d.MessageId).ToList();
        var sessionIds = decisions.Select(d => d.SessionId).Distinct().ToList();

        var userMessages = await db.ChatMessages.AsNoTracking()
            .Where(m => messageIds.Contains(m.MessageId))
            .Select(m => new { m.MessageId, m.Text })
            .ToListAsync(ct);

        var assistantMessages = await db.ChatMessages.AsNoTracking()
            .Where(m => sessionIds.Contains(m.SessionId) && m.Role == "assistant")
            .Select(m => new { m.SessionId, m.Text, m.CreatedAt })
            .ToListAsync(ct);

        var sessions = await db.ChatSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.SessionId))
            .Select(s => new { s.SessionId, s.UserLang, s.Channel })
            .ToListAsync(ct);

        var faqIds = decisions.Where(d => d.SelectedFaqId != null).Select(d => d.SelectedFaqId!.Value).Distinct().ToList();
        var faqs = await db.Faqs.AsNoTracking()
            .Where(f => faqIds.Contains(f.FaqId))
            .Select(f => new { f.FaqId, f.Title })
            .ToListAsync(ct);

        var apiIds = decisions.Where(d => d.SelectedApiId != null).Select(d => d.SelectedApiId!.Value).Distinct().ToList();
        var apis = await db.ApiDefinitions.AsNoTracking()
            .Where(a => apiIds.Contains(a.ApiId))
            .Select(a => new { a.ApiId, a.ApiName })
            .ToListAsync(ct);

        var export = decisions.Select(d =>
        {
            var session = sessions.FirstOrDefault(s => s.SessionId == d.SessionId);
            var userMsg = userMessages.FirstOrDefault(m => m.MessageId == d.MessageId);
            var assistantMsg = assistantMessages
                .Where(m => m.SessionId == d.SessionId && m.CreatedAt >= d.CreatedAt)
                .OrderBy(m => m.CreatedAt)
                .FirstOrDefault();
            var faq = d.SelectedFaqId != null ? faqs.FirstOrDefault(f => f.FaqId == d.SelectedFaqId) : null;
            var api = d.SelectedApiId != null ? apis.FirstOrDefault(a => a.ApiId == d.SelectedApiId) : null;

            return new
            {
                Timestamp = d.CreatedAt,
                d.SessionId,
                Language = session?.UserLang,
                Channel = session?.Channel,
                d.Route,
                d.Confidence,
                Question = userMsg?.Text,
                Answer = assistantMsg?.Text,
                FaqTitle = faq?.Title,
                ApiName = api?.ApiName,
                RagChunkIds = d.SelectedChunkIdsCsv
            };
        }).ToList();

        return new
        {
            exportedAt = DateTimeOffset.UtcNow,
            fromDate = from,
            toDate = to,
            totalRecords = export.Count,
            data = export
        };
    }
}
