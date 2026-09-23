using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.KnowledgeGraph.GetEligibleConcepts;
using VoxMentor.Application.Features.Practice.GetMastery;
using VoxMentor.Application.Features.Practice.GetNextQuestion;
using VoxMentor.Application.Features.Practice.GetReadiness;
using VoxMentor.Application.Features.Practice.SubmitCode;

namespace VoxMentor.Api.Controllers;

/// <summary>
/// Student-facing endpoints: mastery profile, code submissions, and adaptive question selection.
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

    /// <summary>
    /// Returns the next adaptive question for the student. Without jdId, targets
    /// the weakest concept at difficulty 1 + mastery×9.
    /// </summary>
    [HttpGet("next-question")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<NextQuestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNextQuestion(CancellationToken cancellationToken)
    {
        var response = await _sender.Send(new GetNextQuestionQuery(), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Returns eligible and almost-eligible concepts for the authenticated
    /// student based on their mastery of prerequisites.
    /// </summary>
    [HttpGet("eligible")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<EligibleConceptsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetEligible(CancellationToken cancellationToken)
    {
        var response = await _sender.Send(new GetEligibleConceptsQuery(), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Returns the JD-weighted readiness score with per-skill breakdown and
    /// gap analysis. Defaults to the user's most recent job description.
    /// </summary>
    [HttpGet("readiness")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<ReadinessDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReadiness(
        [FromQuery] Guid? jdId,
        CancellationToken cancellationToken)
    {
        var response = await _sender.Send(new GetReadinessQuery { JdId = jdId }, cancellationToken);
        return Ok(response);
    }
}
