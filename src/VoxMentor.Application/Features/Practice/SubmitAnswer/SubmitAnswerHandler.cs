using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.Practice.SubmitAnswer;

/// <summary>
/// Processes answer submissions end-to-end: authenticates the user, loads the
/// question and BKT parameters, updates mastery (creating it on first submit),
/// persists attempt counters, and publishes a mastery-updated event. Linked code
/// submissions derive correctness from the graded row and apply mastery at most
/// once (duplicates replay the original result). Handles concurrent submissions
/// via bounded optimistic-concurrency retries.
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
    /// <exception cref="NotFoundException">The question or linked code submission does not exist (or belongs to another user).</exception>
    /// <exception cref="ValidationException">The linked submission belongs to a different question.</exception>
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

        CodeSubmission? submission = null;
        if (request.CodeSubmissionId is Guid submissionId)
        {
            submission = await _db.CodeSubmissions
                .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken);
            if (submission is null || submission.UserId != userId)
            {
                // Missing and foreign submissions both 404 (no existence oracle).
                throw new NotFoundException($"Code submission {submissionId} was not found.");
            }
            if (submission.QuestionId != request.QuestionId)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["codeSubmissionId"] = ["The code submission does not belong to the given question."]
                });
            }
            if (submission.Status == SubmissionStatus.Pending)
            {
                // No test results to derive correctness from: mirror the
                // submit-code pipeline's no-BKT path instead of recording a
                // bogus incorrect attempt.
                return await BuildReplayResultAsync(submission, question, "Submission has no test results. Mastery unchanged.", cancellationToken);
            }
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
                return await TrySubmitAsync(userId, question, parameters, request, submission, cancellationToken);
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
    /// Single submission attempt: reloads the linked submission after a conflict,
    /// applies the BKT update with an atomic claim in one save, and publishes the
    /// mastery-updated event. Duplicates return the stored result instead.
    /// </summary>
    private async Task<ApiResponse<SubmitAnswerResultDto>> TrySubmitAsync(
        string userId,
        Question question,
        BktParameters parameters,
        SubmitAnswerCommand request,
        CodeSubmission? submission,
        CancellationToken cancellationToken)
    {
        // After ClearChangeTracker the submission is detached: reload it so a
        // concurrent winner's claim is visible before we apply mastery again.
        if (submission is not null && _db.Entry(submission).State == EntityState.Detached)
        {
            var reloaded = await _db.CodeSubmissions
                .FirstOrDefaultAsync(s => s.Id == submission.Id, cancellationToken);
            if (reloaded is null || reloaded.UserId != userId)
            {
                throw new NotFoundException($"Code submission {submission.Id} was not found.");
            }
            if (reloaded.QuestionId != question.Id)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["codeSubmissionId"] = ["The code submission does not belong to the given question."]
                });
            }
            if (reloaded.Status == SubmissionStatus.Pending)
            {
                return await BuildReplayResultAsync(reloaded, question, "Submission has no test results. Mastery unchanged.", cancellationToken);
            }
            submission = reloaded;
        }
        if (submission?.MasteryAppliedAt is not null)
        {
            return await BuildReplayResultAsync(submission, question, "Answer already recorded.", cancellationToken);
        }

        var isCorrect = submission?.IsCorrect ?? request.IsCorrect;

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
        var newMastery = _bktEngine.UpdateMastery(previousMastery, parameters, isCorrect);

        mastery.MasteryProbability = newMastery;
        if (isCorrect)
            mastery.CorrectAttempts++;
        else
            mastery.IncorrectAttempts++;
        mastery.LastPracticedAt = DateTime.UtcNow;
        mastery.UpdatedAt = DateTime.UtcNow;

        // Claim + mastery in one save = atomic (#51).
        if (submission is not null)
        {
            submission.MasteryAppliedAt = DateTime.UtcNow;
            submission.MasteryBefore = previousMastery;
            submission.MasteryAfter = newMastery;
            submission.CorrectAttemptsAfter = mastery.CorrectAttempts;
            submission.IncorrectAttemptsAfter = mastery.IncorrectAttempts;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _eventPublisher.PublishMasteryUpdatedAsync(mastery, previousMastery, cancellationToken);

        var result = new SubmitAnswerResultDto(
            request.QuestionId,
            question.ConceptId,
            isCorrect,
            previousMastery,
            newMastery,
            newMastery - previousMastery,
            mastery.CorrectAttempts,
            mastery.IncorrectAttempts);

        return ApiResponse<SubmitAnswerResultDto>.SuccessResult(result, "Answer recorded successfully.");
    }

    /// <summary>
    /// Rebuilds the original result from claim-time snapshots (falling back to
    /// the current mastery row for pre-migration rows) without applying BKT or
    /// publishing an event.
    /// </summary>
    private async Task<ApiResponse<SubmitAnswerResultDto>> BuildReplayResultAsync(
        CodeSubmission submission,
        Question question,
        string message,
        CancellationToken cancellationToken)
    {
        var isCorrect = submission.Status == SubmissionStatus.Pending
            ? (bool?)null
            : submission.IsCorrect;

        SubmitAnswerResultDto result;
        if (submission.MasteryAfter is float after)
        {
            var before = submission.MasteryBefore ?? after;
            result = new SubmitAnswerResultDto(
                question.Id,
                question.ConceptId,
                isCorrect,
                before,
                after,
                after - before,
                submission.CorrectAttemptsAfter ?? 0,
                submission.IncorrectAttemptsAfter ?? 0);
        }
        else
        {
            // Legacy row claimed by migration backfill: no snapshot exists.
            var mastery = await _db.StudentMasteries
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.UserId == submission.UserId && m.ConceptId == question.ConceptId,
                    cancellationToken);
            var previousMastery = mastery?.MasteryProbability ?? 0f;
            var newMastery = mastery?.MasteryProbability ?? previousMastery;
            result = new SubmitAnswerResultDto(
                question.Id,
                question.ConceptId,
                isCorrect,
                previousMastery,
                newMastery,
                newMastery - previousMastery,
                mastery?.CorrectAttempts ?? 0,
                mastery?.IncorrectAttempts ?? 0);
        }

        return ApiResponse<SubmitAnswerResultDto>.SuccessResult(result, message);
    }
}
