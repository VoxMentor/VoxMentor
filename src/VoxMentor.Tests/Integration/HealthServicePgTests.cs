using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector.EntityFrameworkCore;
using VoxMentor.Infrastructure.Persistence;
using VoxMentor.Infrastructure.Services;
using Xunit;

namespace VoxMentor.Tests.Integration;

// CodeRabbit #96: CustomWebApplicationFactory runs on the in-memory provider, so
// the pg_extension query never executes there and a broken query still passes the
// key-presence assertion. This test exercises the real PostgreSQL path against the
// dev database (appsettings.Development.json, same source eval-tutor.py uses) and
// returns early when no dev config or reachable server exists (CI has no Postgres).
public class HealthServicePgTests
{
    private static string? DevConnectionString()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var file = Path.Combine(dir.FullName, "src", "VoxMentor.Api", "appsettings.Development.json");
            if (File.Exists(file))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs) &&
                    cs.TryGetProperty("DefaultConnection", out var value))
                {
                    return value.GetString();
                }
            }
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public async Task CheckHealthAsync_ReportsSupportedPgvector_WhenPostgresReachable()
    {
        var cs = DevConnectionString();
        if (cs is null) return; // env-conditional: no dev config (CI)

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(cs, o => o.UseVector())
            .Options;
        await using var db = new ApplicationDbContext(options, new TestCurrentUser());
        if (!await db.Database.CanConnectAsync()) return; // env-conditional: no server (CI)

        var svc = new HealthService(db, NullLogger<HealthService>.Instance);
        var result = await svc.CheckHealthAsync();

        Assert.Equal("Healthy", result.Status);
        Assert.EndsWith("(ok)", result.Checks["pgvector"]);
    }
}
