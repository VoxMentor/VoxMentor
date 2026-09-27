using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Api.Authorization;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Api.Controllers;

public record PlagiarismQueueItemDto(
    Guid Id,
    string UserId,
    string? UserEmail,
    Guid QuestionId,
    string Language,
    bool IsCorrect,
    float? PlagiarismScore,
    SubmissionStatus Status,
    DateTime CreatedAt);

public record PlagiarismSubmissionDetailDto(
    Guid Id,
    string UserId,
    string? UserEmail,
    Guid QuestionId,
    string? QuestionTitle,
    string Code,
    string Language,
    bool IsCorrect,
    int TestCasesPassed,
    int TestCasesTotal,
    float? PlagiarismScore,
    string? AiEvaluation,
    SubmissionStatus Status,
    DateTime CreatedAt);

/// <summary>
/// Plagiarism review queue — PlatformAdmin/SuperAdmin (#93). Read-only by design.
/// </summary>
[ApiController]
[Route("api/v1/admin/plagiarism")]
[Authorize(Policy = Policies.ManagePlatform)]
public class PlagiarismReviewController : ControllerBase
{
    private readonly IApplicationDbContext _db;

    public PlagiarismReviewController(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>Lists submissions with PlagiarismScore &gt;= minScore, highest score first.</summary>
    [HttpGet("submissions")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PlagiarismQueueItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetFlaggedSubmissions(
        [FromQuery] float minScore = 0.7f,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        minScore = Math.Clamp(minScore, 0f, 1f);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        // mirror GetQuestionsHandler: offset as long, empty page past int.MaxValue
        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue)
        {
            return Ok(ApiResponse<IReadOnlyList<PlagiarismQueueItemDto>>.SuccessResult(new List<PlagiarismQueueItemDto>()));
        }

        var items = await (
            from s in _db.CodeSubmissions
            where s.PlagiarismScore >= minScore
            let email = _db.Users.Where(u => u.Id == s.UserId).Select(u => u.Email).FirstOrDefault()
            orderby s.PlagiarismScore descending, s.CreatedAt descending
            select new PlagiarismQueueItemDto(
                s.Id, s.UserId, email, s.QuestionId, s.Language, s.IsCorrect,
                s.PlagiarismScore, s.Status, s.CreatedAt)
        ).Skip((int)offset).Take(pageSize).ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<PlagiarismQueueItemDto>>.SuccessResult(items));
    }

    /// <summary>Gets one submission with source code for review.</summary>
    [HttpGet("submissions/{id}")]
    [ProducesResponseType(typeof(ApiResponse<PlagiarismSubmissionDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSubmission(Guid id, CancellationToken cancellationToken)
    {
        var dto = await (
            from s in _db.CodeSubmissions
            where s.Id == id
            let email = _db.Users.Where(u => u.Id == s.UserId).Select(u => u.Email).FirstOrDefault()
            let title = _db.Questions.Where(q => q.Id == s.QuestionId).Select(q => q.Title).FirstOrDefault()
            select new PlagiarismSubmissionDetailDto(
                s.Id, s.UserId, email, s.QuestionId, title, s.Code, s.Language, s.IsCorrect,
                s.TestCasesPassed, s.TestCasesTotal, s.PlagiarismScore, s.AiEvaluation,
                s.Status, s.CreatedAt)
        ).FirstOrDefaultAsync(cancellationToken);

        if (dto is null)
        {
            return NotFound(ApiResponse<object>.FailureResult("Submission not found."));
        }

        return Ok(ApiResponse<PlagiarismSubmissionDetailDto>.SuccessResult(dto));
    }
}
