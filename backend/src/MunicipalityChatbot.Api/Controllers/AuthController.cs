using Microsoft.AspNetCore.Mvc;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Infrastructure.Security;

namespace MunicipalityChatbot.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IEmployeeRepository employees,
    IPasswordHasher hasher,
    IJwtTokenService jwt
) : ControllerBase
{
    public sealed record LoginRequest(string Username, string Password);
    public sealed record LoginResponse(string AccessToken);

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("Username and password are required.");

        var user = await employees.GetByUsernameAsync(req.Username.Trim(), ct);
        if (user is null || !user.IsActive) return Unauthorized();
        if (!hasher.Verify(req.Password, user.PasswordHash)) return Unauthorized();

        var token = jwt.CreateAccessToken(user);
        return Ok(new LoginResponse(token));
    }
}

