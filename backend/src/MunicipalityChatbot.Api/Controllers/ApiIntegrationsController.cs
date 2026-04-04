using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Domain.Entities;

namespace MunicipalityChatbot.Api.Controllers;

[ApiController]
[Route("api/integrations")]
public sealed class ApiIntegrationsController(IApiDefinitionRepository repo) : ControllerBase
{
    public sealed record UpsertApiRequest(
        Guid? ApiId,
        string ApiName,
        string Description,
        string BaseUrl,
        string Method,
        string PathTemplate,
        string AuthType,
        string AuthConfigJson,
        string HeadersTemplateJson,
        string QueryParamsSchemaJson,
        string BodySchemaJson,
        string? BodyTemplateJson,
        string ResponseHandlingNotes,
        bool AllowInChat,
        string AllowlistedDomain
    );

    [HttpGet]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin},{EmployeeRoles.EmployeeEditor},{EmployeeRoles.EmployeeViewer}")]
    public async Task<ActionResult<IReadOnlyList<ApiDefinition>>> List(CancellationToken ct)
        => Ok(await repo.ListAllAsync(ct));

    [HttpGet("allowed")]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin},{EmployeeRoles.EmployeeEditor},{EmployeeRoles.EmployeeViewer}")]
    public async Task<ActionResult<IReadOnlyList<ApiDefinition>>> Allowed(CancellationToken ct)
        => Ok(await repo.ListAllowedInChatAsync(ct));

    [HttpGet("{apiId:guid}")]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin},{EmployeeRoles.EmployeeEditor},{EmployeeRoles.EmployeeViewer}")]
    public async Task<ActionResult<ApiDefinition>> Get(Guid apiId, CancellationToken ct)
    {
        var api = await repo.GetByIdAsync(apiId, ct);
        return api is null ? NotFound() : Ok(api);
    }

    [HttpPost]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin},{EmployeeRoles.EmployeeEditor}")]
    public async Task<ActionResult> Upsert([FromBody] UpsertApiRequest req, CancellationToken ct)
    {
        if (!Uri.TryCreate(req.BaseUrl, UriKind.Absolute, out var uri))
            return BadRequest("BaseUrl must be a valid absolute URL.");

        if (!string.Equals(uri.Host, req.AllowlistedDomain, StringComparison.OrdinalIgnoreCase))
            return BadRequest("AllowlistedDomain must match BaseUrl host.");

        var api = new ApiDefinition
        {
            ApiId = req.ApiId ?? Guid.NewGuid(),
            ApiName = req.ApiName?.Trim() ?? "",
            Description = req.Description?.Trim() ?? "",
            BaseUrl = req.BaseUrl?.Trim() ?? "",
            Method = (req.Method ?? "GET").Trim().ToUpperInvariant(),
            PathTemplate = req.PathTemplate?.Trim() ?? "",
            AuthType = req.AuthType?.Trim() ?? "None",
            AuthConfigJson = string.IsNullOrWhiteSpace(req.AuthConfigJson) ? "{}" : req.AuthConfigJson,
            HeadersTemplateJson = string.IsNullOrWhiteSpace(req.HeadersTemplateJson) ? "{}" : req.HeadersTemplateJson,
            QueryParamsSchemaJson = string.IsNullOrWhiteSpace(req.QueryParamsSchemaJson) ? "{}" : req.QueryParamsSchemaJson,
            BodySchemaJson = string.IsNullOrWhiteSpace(req.BodySchemaJson) ? "{}" : req.BodySchemaJson,
            BodyTemplateJson = req.BodyTemplateJson,
            ResponseHandlingNotes = req.ResponseHandlingNotes?.Trim() ?? "",
            AllowInChat = req.AllowInChat,
            AllowlistedDomain = req.AllowlistedDomain?.Trim() ?? ""
        };

        if (string.IsNullOrWhiteSpace(api.ApiName))
            return BadRequest("ApiName is required.");

        await repo.UpsertAsync(api, ct);
        return Ok(new { apiId = api.ApiId });
    }

    [HttpDelete("{apiId:guid}")]
    [Authorize(Roles = $"{EmployeeRoles.EmployeeAdmin}")]
    public async Task<ActionResult> Delete(Guid apiId, CancellationToken ct)
    {
        await repo.DeleteAsync(apiId, ct);
        return NoContent();
    }
}

