using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>Paged history of the current student's interviews, newest first (#125).</summary>
public class GetMockInterviewHistoryQuery : IRequest<ApiResponse<GetMockInterviewHistoryResultDto>>
{
    /// <summary>1-based page number; defaults to 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Items per page; defaults to 20, clamped to 1–100.</summary>
    public int PageSize { get; set; } = 20;
}
