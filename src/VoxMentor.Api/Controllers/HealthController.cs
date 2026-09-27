using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Api.Authorization;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IHealthService _healthService;
    private readonly IWebHostEnvironment _env;

    public HealthController(IHealthService healthService, IWebHostEnvironment env)
    {
        _healthService = healthService;
        _env = env;
    }

    /// <summary>Liveness probe — anonymous, structured check statuses.</summary>
    [HttpGet]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        var result = await _healthService.CheckHealthAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>Ops detail — PlatformAdmin/SuperAdmin only (#82).</summary>
    [Authorize(Policy = Policies.ManagePlatform)]
    [HttpGet("detail")]
    public async Task<IActionResult> GetHealthDetail(CancellationToken cancellationToken)
    {
        var result = await _healthService.CheckHealthAsync(cancellationToken);
        return Ok(new HealthDetailDto
        {
            Status = result.Status,
            Checks = result.Checks,
            Environment = _env.EnvironmentName,
            MachineName = System.Environment.MachineName,
            UtcTime = DateTime.UtcNow
        });
    }
}

public class HealthDetailDto
{
    public string Status { get; set; } = "Healthy";
    public IDictionary<string, string> Checks { get; set; } = new Dictionary<string, string>();
    public string Environment { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public DateTime UtcTime { get; set; }
}
