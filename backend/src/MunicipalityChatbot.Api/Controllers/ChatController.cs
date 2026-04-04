using System.Text.Json;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MunicipalityChatbot.Application.Models;

namespace MunicipalityChatbot.Api.Controllers;

[ApiController]
[Route("api/chat")]
[EnableCors("Widget")]
public sealed class ChatController(ChatOrchestrator chat) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Anonymous public endpoint (rate limited). Widget can optionally send X-Widget-Api-Key.
    [HttpPost("public")]
    [EnableRateLimiting("PublicChat")]
    public async Task<ActionResult<PublicChatResponse>> PublicChat([FromBody] PublicChatRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest("Message is required.");

        var origin = Request.Headers.Origin.ToString();
        var widgetKey = Request.Headers["X-Widget-Api-Key"].ToString();

        var res = await chat.HandlePublicAsync(req, origin, widgetKey, req.UserToken, req.CustomerId, ct);
        return Ok(res);
    }

    // Streaming endpoint using Server-Sent Events (SSE)
    [HttpPost("public/stream")]
    [EnableRateLimiting("PublicChat")]
    public async Task PublicChatStream([FromBody] PublicChatRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("Message is required.", ct);
            return;
        }

        var origin = Request.Headers.Origin.ToString();
        var widgetKey = Request.Headers["X-Widget-Api-Key"].ToString();

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        await foreach (var evt in chat.HandlePublicStreamAsync(req, origin, widgetKey, req.UserToken, req.CustomerId, ct))
        {
            var json = JsonSerializer.Serialize(evt, JsonOptions);
            await Response.WriteAsync($"data: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        await Response.WriteAsync("data: [DONE]\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
