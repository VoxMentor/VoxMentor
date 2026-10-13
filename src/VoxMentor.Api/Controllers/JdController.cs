using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Jd.UploadJd;

namespace VoxMentor.Api.Controllers;

/// <summary>
/// Job-description intelligence endpoints (#117 upload, #118 readiness/gaps).
/// </summary>
[ApiController]
[Route("api/v1/jd")]
public class JdController : ControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initializes the controller with the MediatR sender.</summary>
    public JdController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Uploads a JD: Ollama parses it into weighted skills, degrading to
    /// keyword heuristics when Ollama is offline. Technical and HR weights
    /// each sum to 1.0. Empty skill maps are valid (200), not an error.
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<UploadJdResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upload(
        [FromBody] UploadJdCommand command,
        CancellationToken cancellationToken)
    {
        var response = await _sender.Send(command, cancellationToken);
        return Ok(response);
    }
}
