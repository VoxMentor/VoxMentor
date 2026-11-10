using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Tutor.GetTutorSessions;

/// <summary>
/// Returns the authenticated student's tutor sessions, newest first.
/// Missing sessions surface as an empty list (no existence oracle).
/// </summary>
public class GetTutorSessionsHandler : IRequestHandler<GetTutorSessionsQuery, ApiResponse<IReadOnlyList<TutorSessionSummaryDto>>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetTutorSessionsHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<IReadOnlyList<TutorSessionSummaryDto>>> Handle(
        GetTutorSessionsQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User must be authenticated to list tutor sessions.");

        var limit = Math.Clamp(request.Limit, 1, 50);

        var sessions = await _db.TutorSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Take(limit)
            .Select(s => new TutorSessionSummaryDto(
                s.Id,
                s.ConceptId,
                s.Question,
                s.Status,
                s.TotalTokens,
                s.CreatedAt,
                s.CompletedAt))
            .ToListAsync(cancellationToken);

        return ApiResponse<IReadOnlyList<TutorSessionSummaryDto>>.SuccessResult(sessions);
    }
}
