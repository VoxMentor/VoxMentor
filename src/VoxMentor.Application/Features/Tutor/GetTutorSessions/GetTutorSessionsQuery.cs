using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Tutor.GetTutorSessions;

/// <summary>
/// Lists the authenticated student's tutor sessions (newest first).
/// </summary>
public class GetTutorSessionsQuery : IRequest<ApiResponse<IReadOnlyList<TutorSessionSummaryDto>>>
{
    /// <summary>Maximum number of sessions to return; clamped to 1-50.</summary>
    public int Limit { get; set; } = 10;
}
