using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>
/// POST /api/v1/mock/start (#124): delegates to
/// <see cref="MockInterviewSessionService"/>, the single dialogue
/// implementation shared with the interview hub (#126).
/// </summary>
public class StartMockInterviewHandler : IRequestHandler<StartMockInterviewCommand, ApiResponse<StartMockInterviewResultDto>>
{
    private readonly MockInterviewSessionService _session;

    /// <summary>Initializes the handler with the shared dialogue session service (#126).</summary>
    public StartMockInterviewHandler(MockInterviewSessionService session)
    {
        _session = session;
    }

    /// <summary>Runs the start flow and wraps the result in the API envelope.</summary>
    public async Task<ApiResponse<StartMockInterviewResultDto>> Handle(
        StartMockInterviewCommand request, CancellationToken cancellationToken)
        => ApiResponse<StartMockInterviewResultDto>.SuccessResult(
            await _session.StartAsync(request, cancellationToken));
}
