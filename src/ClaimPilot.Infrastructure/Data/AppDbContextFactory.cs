using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Npgsql;
using Pgvector.Npgsql;

namespace ClaimPilot.Infrastructure.Data;

/// <summary>
/// Design-time factory used by `dotnet ef` so migrations can be generated inside
/// the Infrastructure project without booting the API host. The connection
/// string is only used for `database update`, never for migration generation.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(
            "Host=localhost;Port=5432;Database=claimpilot;Username=claimpilot;Password=claimpilot");
        dataSourceBuilder.UseVector();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(dataSourceBuilder.Build(), npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.UseVector();
            })
            .Options;

        return new AppDbContext(options);
    }
}