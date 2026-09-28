using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetSubmissions;

/// <summary>
/// Requests the authenticated student's recent code submissions (newest first).
/// </summary>
public class GetSubmissionsQuery : IRequest<ApiResponse<IReadOnlyList<SubmissionItemDto>>>
{
    /// <summary>Maximum number of submissions to return; clamped to 1-50.</summary>
    public int Limit { get; set; } = 10;
}
