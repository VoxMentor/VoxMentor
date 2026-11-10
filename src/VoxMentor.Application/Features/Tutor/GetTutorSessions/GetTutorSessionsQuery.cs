using MediatR;
using System.ComponentModel.DataAnnotations;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Tutor.GetTutorSessions;

/// <summary>
/// Lists the authenticated student's tutor sessions (newest first).
/// </summary>
public class GetTutorSessionsQuery : IRequest<ApiResponse<IReadOnlyList<TutorSessionSummaryDto>>>
{
    /// <summary>Maximum number of sessions to return; clamped to 1-50.</summary>
    [Range(1, 50, ErrorMessage = "Limit must be between 1 and 50.")]
    public int Limit { get; set; } = 10;
}
