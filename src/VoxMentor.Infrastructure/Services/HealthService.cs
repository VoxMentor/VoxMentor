using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace VoxMentor.Infrastructure.Services;

public class HealthService : IHealthService
{
    private readonly ApplicationDbContext _dbContext;

    public HealthService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResultDto> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var checks = new Dictionary<string, string>();
        var isHealthy = true;

        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            if (canConnect)
            {
                checks["postgres"] = "Healthy";
            }
            else
            {
                checks["postgres"] = "Unhealthy";
                isHealthy = false;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            checks["postgres"] = "Unhealthy";
            isHealthy = false;
        }

        try
        {
            var ver = await _dbContext.Database
                .SqlQuery<string>($"SELECT extversion FROM pg_extension WHERE extname = 'vector'")
                .FirstOrDefaultAsync(cancellationToken);
            if (string.IsNullOrEmpty(ver))
            {
                checks["pgvector"] = "Missing";
                isHealthy = false;
            }
            else
            {
                var parts = ver.Split('.');
                var major = int.TryParse(parts[0], out var m) ? m : -1;
                var minor = parts.Length > 1 && int.TryParse(parts[1], out var mi) ? mi : -1;
                var ok = major > 0 || (major == 0 && minor >= 8);
                checks["pgvector"] = ok ? $"{ver} (ok)" : $"{ver} < 0.8.0";
                if (!ok) isHealthy = false;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            checks["pgvector"] = "Unknown";
            isHealthy = false;
        }

        return new HealthCheckResultDto
        {
            Status = isHealthy ? "Healthy" : "Unhealthy",
            Checks = checks
        };
    }
}
