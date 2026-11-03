using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Resume.AnalyzeResume;

namespace VoxMentor.Api.Controllers;

/// <summary>Resume ATS analyzer endpoints (#151).</summary>
[ApiController]
[Route("api/v1/resume")]
public class ResumeController : ControllerBase
{
    private readonly ISender _sender;

    public ResumeController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Analyzes an uploaded resume (.pdf or .txt) against the skills of a parsed JD.
    /// </summary>
    [HttpPost("analyze")]
    [Authorize(Roles = "Student")]
    [EnableRateLimiting("resume-analyze")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<AnalyzeResumeResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Analyze(
        IFormFile? file,
        [FromForm] Guid jdId,
        CancellationToken cancellationToken)
    {
        var command = new AnalyzeResumeCommand(
            file?.FileName.Trim() ?? string.Empty,
            file?.OpenReadStream() ?? Stream.Null,
            file?.Length ?? 0,
            jdId);
        var response = await _sender.Send(command, cancellationToken);
        return Ok(response);
    }
}
