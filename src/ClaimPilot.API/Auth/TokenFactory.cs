using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace ClaimPilot.API.Auth;

internal static class TokenFactory
{
    public static string Create(
        IdentityUser user, IReadOnlyList<string> roles, IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt");
        var issuer = jwt["Issuer"] ?? "claimpilot";
        var audience = jwt["Audience"] ?? "claimpilot-api";
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwt["Key"] ?? "ClaimPilotDevSigningKeyChangeMe_0123456789ABCDEF"));
        var expires = DateTime.UtcNow.AddHours(double.Parse(jwt["ExpiresHours"] ?? "8"));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}