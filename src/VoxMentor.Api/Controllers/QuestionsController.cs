using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Practice.GetQuestionById;
using VoxMentor.Application.Features.Practice.GetQuestions;

namespace VoxMentor.Api.Controllers;

/// <summary>
/// Student-facing question endpoints: list, detail, and filtering.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(Roles = "Student")]
public class QuestionsController : ControllerBase
{
    private readonly ISender _sender;

    public QuestionsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Lists practice questions with optional concept and difficulty filters.
    /// Paginated, ordered by difficulty then title.
    /// </summary>
    [HttpGet("questions")]
    [ProducesResponseType(typeof(ApiResponse<GetQuestionsResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetQuestions(
        [FromQuery] GetQuestionsQuery query,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(query, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Returns a single question by ID. Hidden test cases are excluded;
    /// only examples and visible test cases are returned.
    /// </summary>
    [HttpGet("questions/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<QuestionDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetQuestionById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var response = await _sender.Send(new GetQuestionByIdQuery(id), cancellationToken);
        return Ok(response);
    }
}
