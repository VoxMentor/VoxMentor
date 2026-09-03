using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Practice.SubmitAnswer;

namespace VoxMentor.Api.Controllers;

[ApiController]
[Route("api/v1/answer")]
public class AnswerController : ControllerBase
{
    private readonly ISender _sender;

    public AnswerController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<SubmitAnswerResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitAnswer(
        [FromBody] SubmitAnswerCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(command, cancellationToken);
        return Ok(result);
    }
}
