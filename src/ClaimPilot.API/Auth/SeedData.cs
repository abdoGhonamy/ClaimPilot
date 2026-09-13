using Microsoft.AspNetCore.Identity;

namespace ClaimPilot.API.Auth;

/// <summary>
/// Seeds the four demo roles and users used during local development:
/// adjuster, supervisor, director and viewer. Passwords are long, random and
/// clearly non-production. Credentials are documented in README/SECURITY.
/// </summary>
public sealed class SeedData
{
    public const string AdjusterRole = "Adjuster";
    public const string SupervisorRole = "Supervisor";
    public const string DirectorRole = "Director";
    public const string ViewerRole = "Viewer";

    private readonly UserManager<IdentityUser> _users;
    private readonly RoleManager<IdentityRole> _roles;

    public SeedData(UserManager<IdentityUser> users, RoleManager<IdentityRole> roles)
    {
        _users = users;
        _roles = roles;
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        var roleNames = new[] { AdjusterRole, SupervisorRole, DirectorRole, ViewerRole };
        foreach (var roleName in roleNames)
        {
            if (await _roles.FindByNameAsync(roleName) is null)
                await _roles.CreateAsync(new IdentityRole(roleName));
        }

        var demoUsers = new[]
        {
            new { UserName = "adjuster", Password = "Adjuster#2026-local-only", Roles = new[] { AdjusterRole } },
            new { UserName = "supervisor", Password = "Supervisor#2026-local-only", Roles = new[] { SupervisorRole, AdjusterRole } },
            new { UserName = "director", Password = "Director#2026-local-only", Roles = new[] { DirectorRole, SupervisorRole, AdjusterRole } },
            new { UserName = "viewer", Password = "Viewer#2026-local-only", Roles = new[] { ViewerRole } }
        };

        foreach (var demo in demoUsers)
        {
            if (await _users.FindByNameAsync(demo.UserName) is not null) continue;

            var user = new IdentityUser
            {
                UserName = demo.UserName,
                Email = $"{demo.UserName}@claimpilot.local",
                EmailConfirmed = true
            };
            var result = await _users.CreateAsync(user, demo.Password);
            if (result.Succeeded)
                await _users.AddToRolesAsync(user, demo.Roles);
        }
    }
}