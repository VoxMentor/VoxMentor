using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Api.Authorization;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Api.Controllers;

/// <summary>
/// Audit log read — PlatformAdmin/SuperAdmin (#82). Read-only by design.
/// </summary>
[ApiController]
[Route("api/v1/admin/audit-logs")]
[Authorize(Policy = Policies.ManagePlatform)]
public class AuditLogController : ControllerBase
{
    private readonly IApplicationDbContext _db;

    public AuditLogController(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>Lists audit log entries paged, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AuditLog>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var items = await _db.AuditLogs
            .OrderByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<AuditLog>>.SuccessResult(items));
    }
}
