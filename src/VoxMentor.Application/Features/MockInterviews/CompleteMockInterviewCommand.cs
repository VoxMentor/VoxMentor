using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>Ends an AI mock interview and persists the rubric review (#125).</summary>
public class CompleteMockInterviewCommand : IRequest<ApiResponse<MockInterviewReview>>
{
    /// <summary>Interview being completed; set from the route by the controller.</summary>
    public Guid InterviewId { get; set; }
}
