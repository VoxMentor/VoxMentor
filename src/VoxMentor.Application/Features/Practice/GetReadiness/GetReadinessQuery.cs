using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetReadiness;

/// <summary>
/// Requests the JD-weighted readiness score for the authenticated student.
/// Without <see cref="JdId"/>, defaults to the user's most recent job description.
/// </summary>
public class GetReadinessQuery : IRequest<ApiResponse<ReadinessDto>>
{
    /// <summary>Optional JD to score against; defaults to the user's latest.</summary>
    public Guid? JdId { get; set; }
}
