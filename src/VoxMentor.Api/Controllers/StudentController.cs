using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Practice.GetMastery;
using VoxMentor.Application.Features.Practice.SubmitCode;

namespace VoxMentor.Api.Controllers;

/// <summary>
/// Student-facing endpoints: mastery profile and code submissions.
/// </summary>
[ApiController]
[Route("api/v1/student")]
public class StudentController : ControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initializes the controller with the MediatR sender.</summary>
    public StudentController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Returns the authenticated student's per-concept mastery profile with an
    /// overall readiness summary. Unpracticed concepts report null mastery.
    /// </summary>
    [HttpGet("mastery")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<MasteryProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMastery(CancellationToken cancellationToken)
    {
        var response = await _sender.Send(new GetMasteryQuery(), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Executes submitted code against the question's test cases, evaluates it
    /// with the AI evaluator, persists the submission, and updates concept
    /// mastery via BKT.
    /// </summary>
    [HttpPost("submit-code")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<SubmitCodeResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitCode(
        [FromBody] SubmitCodeCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(command, cancellationToken);
        return Ok(result);
    }
}
