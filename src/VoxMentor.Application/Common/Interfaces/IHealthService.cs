using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Common.Interfaces;

public interface IHealthService
{
    Task<HealthCheckResultDto> CheckHealthAsync(CancellationToken cancellationToken = default);
}
