using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Api.Authorization;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Api.Controllers;

public record TutorSessionSummaryDto(
    Guid Id,
    string UserId,
    string? UserEmail,
    Guid? ConceptId,
    string Question,
    TutorSessionStatus Status,
    int TotalTokens,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public record TutorSessionDetailDto(
    Guid Id,
    string UserId,
    string? UserEmail,
    Guid? ConceptId,
    string Question,
    string? Answer,
    TutorSessionStatus Status,
    int TotalTokens,
    DateTime CreatedAt,
    DateTime? CompletedAt);

/// <summary>
/// Cross-user tutor support — PlatformAdmin/SuperAdmin (#93). Read-only by design.
/// </summary>
[ApiController]
[Route("api/v1/admin/tutor")]
[Authorize(Policy = Policies.ManagePlatform)]
public class TutorSupportController : ControllerBase
{
    private readonly IApplicationDbContext _db;

    public TutorSupportController(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>Lists any user's tutor sessions paged, newest first, with optional userId/status filters.</summary>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TutorSessionSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSessions(
        [FromQuery] string? userId = null,
        [FromQuery] TutorSessionStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        // mirror GetQuestionsHandler: offset as long, empty page past int.MaxValue
        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue)
        {
            return Ok(ApiResponse<IReadOnlyList<TutorSessionSummaryDto>>.SuccessResult(new List<TutorSessionSummaryDto>()));
        }

        IQueryable<TutorSession> query = _db.TutorSessions;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(s => s.UserId == userId);
        }
        if (status.HasValue)
        {
            query = query.Where(s => s.Status == status.Value);
        }

        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(s => new TutorSessionSummaryDto(
                s.Id,
                s.UserId,
                _db.Users.Where(u => u.Id == s.UserId).Select(u => u.Email).FirstOrDefault(),
                s.ConceptId,
                s.Question,
                s.Status,
                s.TotalTokens,
                s.CreatedAt,
                s.CompletedAt))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<TutorSessionSummaryDto>>.SuccessResult(items));
    }

    /// <summary>Gets one tutor session with the full answer for support review.</summary>
    [HttpGet("sessions/{id}")]
    [ProducesResponseType(typeof(ApiResponse<TutorSessionDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSession(Guid id, CancellationToken cancellationToken)
    {
        var dto = await _db.TutorSessions
            .Where(s => s.Id == id)
            .Select(s => new TutorSessionDetailDto(
                s.Id,
                s.UserId,
                _db.Users.Where(u => u.Id == s.UserId).Select(u => u.Email).FirstOrDefault(),
                s.ConceptId,
                s.Question,
                s.Answer,
                s.Status,
                s.TotalTokens,
                s.CreatedAt,
                s.CompletedAt))
            .FirstOrDefaultAsync(cancellationToken);

        if (dto is null)
        {
            return NotFound(ApiResponse<object>.FailureResult("Session not found."));
        }

        return Ok(ApiResponse<TutorSessionDetailDto>.SuccessResult(dto));
    }
}
