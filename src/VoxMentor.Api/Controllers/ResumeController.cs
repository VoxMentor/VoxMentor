using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Resume.AnalyzeResume;

namespace VoxMentor.Api.Controllers;

/// <summary>
/// Resume ATS analysis (#151): upload a PDF resume and match it against a
/// JD's weighted skills to get a fit report.
/// </summary>
[ApiController]
[Route("api/v1/resume")]
[Authorize(Roles = "Student")]
public class ResumeController : ControllerBase
{
    private readonly ISender _sender;

    public ResumeController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Analyzes a resume against a JD's weighted skills.
    /// multipart/form-data: `file` (.pdf, ≤5 MB) + required `jdId`.
    /// </summary>
    [HttpPost("analyze")]
    [ProducesResponseType(typeof(ApiResponse<AnalyzeResumeResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status429TooManyRequests)]
    // Reject oversized requests before Kestrel/form buffering (default 128 MB)
    // reaches the validator's 5 MB rule; 1 MB headroom for multipart framing.
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
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
