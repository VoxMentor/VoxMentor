using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>
/// GET /api/v1/mock/history (#125): the current student's interviews, newest
/// first, paged. The IUserOwned global filter scopes rows to the caller, so
/// other students' interviews can never appear.
/// </summary>
public class GetMockInterviewHistoryHandler : IRequestHandler<GetMockInterviewHistoryQuery, ApiResponse<GetMockInterviewHistoryResultDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    /// <summary>Initializes the handler with the data context and the current user.</summary>
    public GetMockInterviewHistoryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>Maps the page of owned interviews to the documented shape.</summary>
    public async Task<ApiResponse<GetMockInterviewHistoryResultDto>> Handle(
        GetMockInterviewHistoryQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User must be authenticated to view mock interview history.");

        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var rows = await _db.MockInterviews
            .AsNoTracking()
            .OrderByDescending(m => m.StartedAt)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Enum ToString and the score gate run client-side: EF can't translate
        // them for PostgreSQL, and score stays null until Completed.
        var items = rows
            .Select(m => new MockInterviewHistoryItemDto(
                m.Id,
                m.Type.ToString(),
                m.Status.ToString(),
                m.Status == MockInterviewStatus.Completed ? m.Score : null,
                m.StartedAt))
            .ToList();

        return ApiResponse<GetMockInterviewHistoryResultDto>.SuccessResult(
            new GetMockInterviewHistoryResultDto(items),
            "Mock interview history retrieved successfully.");
    }
}
