using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.Tutor.AskTutor;

/// <summary>
/// Creates a Pending tutor session for the authenticated student, then queues
/// background answer generation (#72). Fails fast with 503 while the Ollama
/// circuit breaker is open so no session can strand ungenerated.
/// </summary>
public class AskTutorHandler : IRequestHandler<AskTutorCommand, ApiResponse<AskTutorResultDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IRateLimiter _rateLimiter;
    private readonly ITutorSessionQueue _queue;
    private readonly ITutorCircuit _circuit;

    /// <summary>Initializes the handler with the data context, current user, rate limiter, session queue, and circuit breaker.</summary>
    public AskTutorHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IRateLimiter rateLimiter,
        ITutorSessionQueue queue,
        ITutorCircuit circuit)
    {
        _db = db;
        _currentUser = currentUser;
        _rateLimiter = rateLimiter;
        _queue = queue;
        _circuit = circuit;
    }

    /// <summary>Creates a Pending tutor session, queues background answer generation, and returns the polling reference.</summary>
    public async Task<ApiResponse<AskTutorResultDto>> Handle(AskTutorCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            throw new UnauthorizedAccessException("User must be authenticated to ask the tutor.");
        }

        // Before rate limiting: an Ollama outage shouldn't burn the student's quota.
        if (_circuit.IsOpen)
        {
            throw new ServiceUnavailableException(
                "The tutor is temporarily unavailable. Please try again in a few seconds.");
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

        try
        {
            _queue.Enqueue(session.Id);
        }
        catch
        {
            // Enqueue failed after the row committed: remove it so the poll
            // endpoint can't return a Pending session that will never run
            // (mirrors UploadTextbookHandler).
            try
            {
                _db.TutorSessions.Remove(session);
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            catch
            {
                // best effort — preserve the original failure
            }

            throw;
        }

        // Claim the enqueue for the reconciliation sweep (#72): written after
        // the enqueue, so a crash in between leaves QueuedAt null and the sweep
        // re-queues it. Best effort — QueuedAt=null only costs one duplicate
        // enqueue, which the job's claim absorbs.
        session.QueuedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // row and job are both live; staleness recovery still applies
        }

        var message = $"Answer generation started. Poll GET /api/v1/tutor/sessions/{session.Id}.";
        return ApiResponse<AskTutorResultDto>.SuccessResult(
            new AskTutorResultDto(session.Id, message),
            "Accepted");
    }
}
