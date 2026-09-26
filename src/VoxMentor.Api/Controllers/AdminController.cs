using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Admin.CreateQuestion;
using VoxMentor.Application.Features.Admin.GetTextbookJob;
using VoxMentor.Application.Features.Admin.GetQuestions;
using VoxMentor.Application.Features.Admin.UploadTextbook;

namespace VoxMentor.Api.Controllers;

/// <summary>
/// Admin endpoints for question bank and textbook content management. Requires Admin role.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly ISender _sender;

    public AdminController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Creates a new practice question in the bank.
    /// </summary>
    [HttpPost("questions")]
    [ProducesResponseType(typeof(ApiResponse<CreateQuestionResultDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateQuestion(
        [FromBody] CreateQuestionCommand command, CancellationToken cancellationToken)
    {
        var response = await _sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// Lists questions paged for admin curation.
    /// </summary>
    [HttpGet("questions")]
    [ProducesResponseType(typeof(ApiResponse<GetQuestionsResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetQuestions(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetQuestionsQuery(page, pageSize), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Uploads prep content (.pdf/.txt) for background chunking + embedding (#70).
    /// </summary>
    [HttpPost("textbook/upload")]
    [ProducesResponseType(typeof(ApiResponse<UploadTextbookResultDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadTextbook(
        IFormFile? file, [FromForm] Guid? conceptId, CancellationToken cancellationToken)
    {
        var command = new UploadTextbookCommand(
            file?.FileName ?? string.Empty,
            file?.OpenReadStream() ?? Stream.Null,
            file?.Length ?? 0,
            conceptId);
        var response = await _sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status202Accepted, response);
    }

    /// <summary>
    /// Polls a textbook ingestion job's progress (Pending → Processing → Completed/Failed).
    /// </summary>
    [HttpGet("textbook/status/{jobId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<TextbookJobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTextbookJob(Guid jobId, CancellationToken cancellationToken)
    {
        var response = await _sender.Send(new GetTextbookJobQuery(jobId), cancellationToken);
        return Ok(response);
    }
}
