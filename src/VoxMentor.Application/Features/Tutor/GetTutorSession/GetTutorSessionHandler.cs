using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Tutor.GetTutorSession;

/// <summary>
/// Loads a tutor session for the authenticated student. Missing or foreign
/// sessions both surface as 404 (no existence oracle).
/// </summary>
public class GetTutorSessionHandler : IRequestHandler<GetTutorSessionQuery, ApiResponse<TutorSessionDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetTutorSessionHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<TutorSessionDto>> Handle(GetTutorSessionQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            throw new UnauthorizedAccessException("User must be authenticated to poll a tutor session.");
        }

        var session = await _db.TutorSessions
            .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.UserId == userId, cancellationToken);
        if (session is null)
        {
            throw new NotFoundException($"Session {request.SessionId} was not found.");
        }

        var dto = new TutorSessionDto(
            session.Id,
            session.ConceptId,
            session.Question,
            session.Answer,
            session.Status,
            session.TotalTokens,
            session.CreatedAt,
            session.CompletedAt);

        return ApiResponse<TutorSessionDto>.SuccessResult(dto);
    }
}
