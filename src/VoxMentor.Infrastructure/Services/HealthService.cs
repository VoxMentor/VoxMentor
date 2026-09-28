using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace VoxMentor.Infrastructure.Services;

public class HealthService : IHealthService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<HealthService> _logger;

    public HealthService(ApplicationDbContext dbContext, ILogger<HealthService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
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
                .SqlQuery<string>(PgvectorVersion.Sql)
                .FirstOrDefaultAsync(cancellationToken);
            if (string.IsNullOrEmpty(ver))
            {
                checks["pgvector"] = "Missing";
                isHealthy = false;
            }
            else
            {
                var ok = PgvectorVersion.IsSupported(ver);
                checks["pgvector"] = ok ? $"{ver} (ok)" : $"{ver} < 0.8.0";
                if (!ok) isHealthy = false;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "pgvector version check failed");
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
