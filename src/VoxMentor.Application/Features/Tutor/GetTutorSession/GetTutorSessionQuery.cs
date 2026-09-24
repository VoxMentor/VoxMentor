using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Tutor.GetTutorSession;

/// <summary>Polls a tutor session's status and answer.</summary>
/// <param name="SessionId">Session to fetch; 404 if missing or not owned.</param>
public record GetTutorSessionQuery(
    Guid SessionId
) : IRequest<ApiResponse<TutorSessionDto>>;
