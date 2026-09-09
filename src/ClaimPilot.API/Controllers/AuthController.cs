using System.IdentityModel.Tokens.Jwt;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

using ClaimPilot.API.Auth;
using ClaimPilot.API.Dtos;
using ClaimPilot.Domain.Exceptions;

namespace ClaimPilot.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<IdentityUser> _users;
    private readonly IConfiguration _configuration;

    public AuthController(
        UserManager<IdentityUser> users, IConfiguration configuration)
    {
        _users = users;
        _configuration = configuration;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var user = await _users.FindByNameAsync(request.Username) ?? throw new DomainException("Invalid credentials.");
        if (!await _users.CheckPasswordAsync(user, request.Password))
            throw new DomainException("Invalid credentials.");

        var roles = (await _users.GetRolesAsync(user)).AsReadOnly();
        var token = TokenFactory.Create(user, roles, _configuration);
        var expires = DateTime.UtcNow.AddHours(double.Parse(_configuration["Jwt:ExpiresHours"] ?? "8"));

        return Ok(new LoginResponse(token, expires, user.UserName!, roles));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<LoginResponse>> Me()
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var user = await _users.FindByIdAsync(userId ?? string.Empty);
        if (user is null) return Unauthorized();

        var roles = (await _users.GetRolesAsync(user)).AsReadOnly();
        return Ok(new LoginResponse(string.Empty, DateTime.UtcNow, user.UserName!, roles));
    }
}