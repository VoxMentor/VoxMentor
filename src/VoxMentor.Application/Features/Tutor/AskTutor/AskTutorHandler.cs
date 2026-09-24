using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.Tutor.AskTutor;

/// <summary>
/// Creates a Pending tutor session for the authenticated student after rate-limiting
/// and optional concept validation. Answer generation is deferred (#72).
/// </summary>
public class AskTutorHandler : IRequestHandler<AskTutorCommand, ApiResponse<AskTutorResultDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IRateLimiter _rateLimiter;

    public AskTutorHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IRateLimiter rateLimiter)
    {
        _db = db;
        _currentUser = currentUser;
        _rateLimiter = rateLimiter;
    }

    public async Task<ApiResponse<AskTutorResultDto>> Handle(AskTutorCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            throw new UnauthorizedAccessException("User must be authenticated to ask the tutor.");
        }

        await _rateLimiter.CheckAsync($"tutor:ask:{userId}", cancellationToken);

        if (request.ConceptId.HasValue)
        {
            var conceptExists = await _db.Concepts
                .AnyAsync(c => c.Id == request.ConceptId.Value, cancellationToken);
            if (!conceptExists)
            {
                throw new NotFoundException($"Concept {request.ConceptId} was not found.");
            }
        }

        var session = new TutorSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConceptId = request.ConceptId,
            Question = request.Question,
            Status = TutorSessionStatus.Pending
        };
        _db.TutorSessions.Add(session);
        await _db.SaveChangesAsync(cancellationToken);

        var message = $"Session saved as Pending. Answer generation is not available yet. Poll GET /api/v1/tutor/sessions/{session.Id}.";
        return ApiResponse<AskTutorResultDto>.SuccessResult(
            new AskTutorResultDto(session.Id, message),
            "Accepted");
    }
}
