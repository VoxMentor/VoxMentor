using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>Fetches the persisted review of a completed interview (#125).</summary>
public class GetMockInterviewReviewQuery : IRequest<ApiResponse<GetMockInterviewReviewResultDto>>
{
    /// <summary>Interview whose review is wanted; set from the route by the controller.</summary>
    public Guid InterviewId { get; set; }
}
