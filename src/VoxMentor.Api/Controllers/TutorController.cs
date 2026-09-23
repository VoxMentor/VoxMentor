using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Tutor.AskTutor;
using VoxMentor.Application.Features.Tutor.GetTutorSession;

namespace VoxMentor.Api.Controllers;

/// <summary>
/// AI Coach (RAG Tutor): start an answer session and poll its status.
/// </summary>
[ApiController]
[Route("api/v1/tutor")]
public class TutorController : ControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initializes the controller with the MediatR sender.</summary>
    public TutorController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Starts an AI tutor answer session (202). Rate limited to 10/hour/student.
    /// </summary>
    [HttpPost("ask")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<AskTutorResultDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Ask(
        [FromBody] AskTutorCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(command, cancellationToken);
        return Accepted(result);
    }

    /// <summary>
    /// Polls a tutor session's status and answer. 404 if missing or not owned.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<TutorSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSession(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var response = await _sender.Send(new GetTutorSessionQuery(sessionId), cancellationToken);
        return Ok(response);
    }
}
