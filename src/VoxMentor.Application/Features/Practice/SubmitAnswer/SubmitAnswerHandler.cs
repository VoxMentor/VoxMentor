using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Features.Practice.SubmitAnswer;

/// <summary>
/// Processes answer submissions end-to-end: authenticates the user, loads the
/// question and BKT parameters, updates mastery (creating it on first submit),
/// persists attempt counters, and publishes a mastery-updated event. Handles
/// concurrent submissions via bounded optimistic-concurrency retries.
/// </summary>
public class SubmitAnswerHandler : IRequestHandler<SubmitAnswerCommand, ApiResponse<SubmitAnswerResultDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IBktEngine _bktEngine;
    private readonly ICurrentUserService _currentUser;
    private readonly IMasteryEventPublisher _eventPublisher;

    public SubmitAnswerHandler(
        IApplicationDbContext db,
        IBktEngine bktEngine,
        ICurrentUserService currentUser,
        IMasteryEventPublisher eventPublisher)
    {
        _db = db;
        _bktEngine = bktEngine;
        _currentUser = currentUser;
        _eventPublisher = eventPublisher;
    }

    /// <summary>
    /// Executes the submission pipeline with retry handling for write races.
    /// Retries concurrency conflicts and first-submit insert races up to three
    /// attempts, recalculating BKT mastery from fresh state each time; throws
    /// <see cref="ConflictException"/> when retries are exhausted.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">No authenticated user.</exception>
    /// <exception cref="NotFoundException">The question does not exist.</exception>
    /// <exception cref="ConflictException">Retries were exhausted by concurrent submissions.</exception>
    public async Task<ApiResponse<SubmitAnswerResultDto>> Handle(SubmitAnswerCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            throw new UnauthorizedAccessException("User must be authenticated to submit an answer.");
        }

        var question = await _db.Questions
            .FirstOrDefaultAsync(q => q.Id == request.QuestionId, cancellationToken);
        if (question is null)
        {
            throw new NotFoundException($"Question {request.QuestionId} was not found.");
        }

        var parameters = await _db.BktParameters
            .FirstOrDefaultAsync(p => p.ConceptId == question.ConceptId, cancellationToken)
            ?? new BktParameters { ConceptId = question.ConceptId };

        // Bounded retries on write races: recalculate from fresh state instead of losing updates.
        const int maxAttempts = 3;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await TrySubmitAsync(userId, question, parameters, request, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts - 1)
            {
                // Lost-update race: another request saved first.
                _db.ClearChangeTracker();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConflictException("Concurrent submissions conflicted. Please retry.");
            }
            catch (DbUpdateException) when (attempt < maxAttempts - 1)
            {
                // Possible insert-insert race on first submit (unique-violation):
                // retry only if the winner's row actually exists, else surface the real failure.
                _db.ClearChangeTracker();
                var existing = await _db.StudentMasteries
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.UserId == userId && m.ConceptId == question.ConceptId, cancellationToken);
                if (existing is null)
                    throw;
            }
        }
    }

    /// <summary>
    /// Single submission attempt: loads (or creates) the student's mastery row for
    /// the concept, applies the BKT update, increments attempt counters, persists,
    /// and publishes the mastery-updated event.
    /// </summary>
    private async Task<ApiResponse<SubmitAnswerResultDto>> TrySubmitAsync(
        string userId,
        Question question,
        BktParameters parameters,
        SubmitAnswerCommand request,
        CancellationToken cancellationToken)
    {
        var mastery = await _db.StudentMasteries
            .FirstOrDefaultAsync(m => m.UserId == userId && m.ConceptId == question.ConceptId, cancellationToken);
        if (mastery is null)
        {
            mastery = new StudentMastery
            {
                UserId = userId,
                ConceptId = question.ConceptId,
                MasteryProbability = parameters.PriorKnowledge
            };
            _db.StudentMasteries.Add(mastery);
        }

        var previousMastery = mastery.MasteryProbability;
        var newMastery = _bktEngine.UpdateMastery(previousMastery, parameters, request.IsCorrect);

        mastery.MasteryProbability = newMastery;
        if (request.IsCorrect)
            mastery.CorrectAttempts++;
        else
            mastery.IncorrectAttempts++;
        mastery.LastPracticedAt = DateTime.UtcNow;
        mastery.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await _eventPublisher.PublishMasteryUpdatedAsync(mastery, previousMastery, cancellationToken);

        var result = new SubmitAnswerResultDto(
            request.QuestionId,
            question.ConceptId,
            request.IsCorrect,
            previousMastery,
            newMastery,
            newMastery - previousMastery,
            mastery.CorrectAttempts,
            mastery.IncorrectAttempts);

        return ApiResponse<SubmitAnswerResultDto>.SuccessResult(result, "Answer recorded successfully.");
    }
}
