using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>Submits the candidate's answer turn; Ollama replies with a probe (#124).</summary>
public class AnswerMockInterviewCommand : IRequest<ApiResponse<AnswerMockInterviewResultDto>>
{
    /// <summary>Interview being answered; set from the route by the controller.</summary>
    public Guid InterviewId { get; set; }

    /// <summary>The candidate's spoken/typed answer for this turn.</summary>
    public string Message { get; set; } = string.Empty;
}
