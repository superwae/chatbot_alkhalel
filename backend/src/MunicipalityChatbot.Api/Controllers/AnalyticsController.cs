using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MunicipalityChatbot.Application.Abstractions;

namespace MunicipalityChatbot.Api.Controllers;

[ApiController]
[Route("api/analytics")]
public sealed class AnalyticsController(IAnalyticsRepository analytics) : ControllerBase
{
    private const string AllowedRoles = $"{MunicipalityChatbot.Domain.Entities.EmployeeRoles.EmployeeAdmin},{MunicipalityChatbot.Domain.Entities.EmployeeRoles.EmployeeEditor},{MunicipalityChatbot.Domain.Entities.EmployeeRoles.EmployeeViewer}";

    [HttpGet("summary")]
    [Authorize(Roles = AllowedRoles)]
    public async Task<ActionResult<object>> Summary(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default)
        => Ok(await analytics.GetSummaryAsync(from, to, ct));

    [HttpGet("chat-history")]
    [Authorize(Roles = AllowedRoles)]
    public async Task<ActionResult<object>> ChatHistory(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? route = null,
        CancellationToken ct = default)
        => Ok(await analytics.GetChatHistoryAsync(limit, offset, from, to, route, ct));

    [HttpGet("conversation/{sessionId:guid}")]
    [Authorize(Roles = AllowedRoles)]
    public async Task<ActionResult<object>> Conversation(Guid sessionId, CancellationToken ct = default)
        => Ok(await analytics.GetConversationAsync(sessionId, ct));

    [HttpGet("export")]
    [Authorize(Roles = AllowedRoles)]
    public async Task<ActionResult<object>> Export(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default)
        => Ok(await analytics.ExportChatLogsAsync(from, to, ct));
}
