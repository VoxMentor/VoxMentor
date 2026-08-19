using Microsoft.AspNetCore.Mvc;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IHealthService _healthService;

    public HealthController(IHealthService healthService)
    {
        _healthService = healthService;
    }

    [HttpGet]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        var result = await _healthService.CheckHealthAsync(cancellationToken);
        return Ok(result);
    }
}
