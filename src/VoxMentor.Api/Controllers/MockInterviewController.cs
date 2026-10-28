using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.MockInterviews;

namespace VoxMentor.Api.Controllers;

/// <summary>
/// Mock interviews (#124 start/answer, #125 complete/review/history):
/// start a run, submit answer turns, then complete it and read the review.
/// </summary>
[ApiController]
[Route("api/v1/mock")]
public class MockInterviewController : ControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initializes the controller with the MediatR sender.</summary>
    public MockInterviewController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Starts an AI mock interview (202). 404 for an unknown/unowned jdId,
    /// 429 once the daily limit is reached; jdId is optional.
    /// </summary>
    [HttpPost("start")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<StartMockInterviewResultDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Start(
        [FromBody] StartMockInterviewCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(command, cancellationToken);
        return Accepted(result);
    }

    /// <summary>
    /// Submits one answer turn (200): the transcript grows by the candidate
    /// turn and the interviewer's probe, order preserved. 404 unknown/unowned
    /// interview, 409 already ended, 400 empty message.
    /// </summary>
    [HttpPost("{interviewId:guid}/answer")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<AnswerMockInterviewResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Answer(
        Guid interviewId,
        [FromBody] AnswerMockInterviewCommand command,
        CancellationToken cancellationToken)
    {
        command.InterviewId = interviewId;
        var response = await _sender.Send(command, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Ends the interview and returns the rubric review (200). 404 unknown/
    /// unowned interview, 409 already ended.
    /// </summary>
    [HttpPost("{interviewId:guid}/complete")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<MockInterviewReview>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(Guid interviewId, CancellationToken cancellationToken)
    {
        var response = await _sender.Send(
            new CompleteMockInterviewCommand { InterviewId = interviewId }, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Fetches the stored review of a completed interview (200). 404 unknown/
    /// unowned interview, or one that has not completed yet.
    /// </summary>
    [HttpGet("{interviewId:guid}/review")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<GetMockInterviewReviewResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReview(Guid interviewId, CancellationToken cancellationToken)
    {
        var response = await _sender.Send(
            new GetMockInterviewReviewQuery { InterviewId = interviewId }, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Lists the student's interviews newest first, paged (200): optional
    /// page (default 1) and pageSize (default 20, max 100) query params.
    /// </summary>
    [HttpGet("history")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ApiResponse<GetMockInterviewHistoryResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] GetMockInterviewHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var response = await _sender.Send(query, cancellationToken);
        return Ok(response);
    }
}
