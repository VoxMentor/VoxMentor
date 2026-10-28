using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>
/// POST /api/v1/mock/{interviewId}/answer (#124): delegates to
/// <see cref="MockInterviewSessionService"/>, the single dialogue
/// implementation shared with the interview hub (#126).
/// </summary>
public class AnswerMockInterviewHandler : IRequestHandler<AnswerMockInterviewCommand, ApiResponse<AnswerMockInterviewResultDto>>
{
    private readonly MockInterviewSessionService _session;

    /// <summary>Initializes the handler with the shared dialogue session service (#126).</summary>
    public AnswerMockInterviewHandler(MockInterviewSessionService session)
    {
        _session = session;
    }

    /// <summary>Runs the answer flow and wraps the result in the API envelope.</summary>
    public async Task<ApiResponse<AnswerMockInterviewResultDto>> Handle(
        AnswerMockInterviewCommand request, CancellationToken cancellationToken)
        => ApiResponse<AnswerMockInterviewResultDto>.SuccessResult(
            await _session.AnswerAsync(request, cancellationToken));
}
