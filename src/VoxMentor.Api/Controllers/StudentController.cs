using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Practice.GetMastery;

namespace VoxMentor.Api.Controllers;

/// <summary>
/// Student-facing endpoints: mastery profile and (upcoming) code submissions.
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
}
