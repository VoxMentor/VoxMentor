using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetMastery;

/// <summary>
/// Requests the authenticated student's mastery profile across all concepts.
/// The user is resolved server-side, so the query carries no parameters.
/// </summary>
public class GetMasteryQuery : IRequest<ApiResponse<MasteryProfileDto>>
{
}
