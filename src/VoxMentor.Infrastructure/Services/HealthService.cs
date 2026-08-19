using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Infrastructure.Persistence;

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

        return new HealthCheckResultDto
        {
            Status = isHealthy ? "Healthy" : "Unhealthy",
            Checks = checks
        };
    }
}
