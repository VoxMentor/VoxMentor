using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Common.Services;
using VoxMentor.Application.Features.Practice.GetReadiness;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>
/// POST /api/v1/mock/{interviewId}/complete (#125): runs the rubric AI review
/// over the transcript, applies BKT mastery updates from the per-problem
/// solved verdicts, records the readiness delta through the shared
/// GetReadinessQuery/ReadinessCalculator, and flips the interview to Completed
/// with Score, ReviewJson and CompletedAt persisted. Degrades to a canned
/// review (and skips mastery updates) when the model is unavailable — never 500.
/// </summary>
public class CompleteMockInterviewHandler : IRequestHandler<CompleteMockInterviewCommand, ApiResponse<MockInterviewReview>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _sender;
    private readonly IMockInterviewGenerator _generator;
    private readonly IBktEngine _bktEngine;
    private readonly IMasteryEventPublisher _eventPublisher;
    private readonly AsyncKeyedLock _lock;

    /// <summary>Initializes the handler with its data, user, selector, generator, BKT, events, and lock dependencies.</summary>
    public CompleteMockInterviewHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ISender sender,
        IMockInterviewGenerator generator,
        IBktEngine bktEngine,
        IMasteryEventPublisher eventPublisher,
        AsyncKeyedLock @lock)
    {
        _db = db;
        _currentUser = currentUser;
        _sender = sender;
        _generator = generator;
        _bktEngine = bktEngine;
        _eventPublisher = eventPublisher;
        _lock = @lock;
    }

    /// <summary>Reviews, scores, and completes the interview (404/409 per API.md §6).</summary>
    public async Task<ApiResponse<MockInterviewReview>> Handle(
        CompleteMockInterviewCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User must be authenticated to complete a mock interview.");

        // Same lock key as the answer path so a completing request and a late
        // answer on one interview cannot interleave (shared #124/#125 lock).
        using (await _lock.AcquireAsync($"mock:interview:{request.InterviewId}", cancellationToken))
        {
            var interview = await _db.MockInterviews
                .FirstOrDefaultAsync(m => m.Id == request.InterviewId, cancellationToken)
                ?? throw new NotFoundException("Interview not found.");

            if (interview.Status is MockInterviewStatus.Completed or MockInterviewStatus.Failed)
                throw new ConflictException("Interview has already ended.");

            string? company = null;
            string? role = null;
            if (interview.JdId is { } jdId)
            {
                var jd = await _db.JobDescriptions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(j => j.Id == jdId, cancellationToken);
                company = jd?.CompanyName;
                role = jd?.Role;
            }

            var turns = string.IsNullOrEmpty(interview.TranscriptJson)
                ? new List<TranscriptTurn>()
                : JsonSerializer.Deserialize<List<TranscriptTurn>>(interview.TranscriptJson, TranscriptTurn.JsonOptions)
                  ?? new List<TranscriptTurn>();

            // Captured BEFORE the review runs — the Done-when assertion of #125
            // is that this matches the readiness score prior to the review.
            var before = await ReadReadinessAsync(interview.JdId, cancellationToken);

            var generated = await _generator.ReviewAsync(turns, interview.Type, company, role, cancellationToken);

            MockInterviewReviewDraft draft;
            float after;
            if (generated is null)
            {
                // Degraded: no solved verdicts → no mastery writes, so the
                // delta is honestly 0 rather than a fabricated isCorrect=false.
                draft = CannedReview(turns);
                after = before;
            }
            else
            {
                draft = generated;
                await ApplyMasteryUpdatesAsync(userId, turns, draft, cancellationToken);
                // AsNoTracking readiness reads the DB, so mastery is saved first.
                after = await ReadReadinessAsync(interview.JdId, cancellationToken);
            }

            var score = Math.Clamp(draft.Score, 0f, 100f);
            var review = new MockInterviewReview(
                score,
                interview.Type.ToString(),
                draft.TechnicalReview,
                new ReadinessDeltaDto(before, after, after - before),
                draft.NextSteps);

            interview.Score = score;
            interview.ReviewJson = JsonSerializer.Serialize(review, TranscriptTurn.JsonOptions);
            interview.Status = MockInterviewStatus.Completed;
            interview.CompletedAt = DateTime.UtcNow;
            interview.Feedback = review.NextSteps;
            await _db.SaveChangesAsync(cancellationToken);

            return ApiResponse<MockInterviewReview>.SuccessResult(review, "Mock interview completed.");
        }
    }

    /// <summary>
    /// Readiness through the SAME GetReadinessQuery handler the readiness and
    /// JD endpoints use (#125 — "the same instance, not a second copy of the
    /// formula"). No JD yet → 0: complete must still work for JD-less runs.
    /// </summary>
    private async Task<float> ReadReadinessAsync(Guid? jdId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _sender.Send(new GetReadinessQuery { JdId = jdId }, cancellationToken);
            return response.Success && response.Data is not null ? response.Data.Score : 0f;
        }
        catch (NotFoundException)
        {
            return 0f;
        }
    }

    /// <summary>
    /// BKT write per distinct question in the transcript, using the review's
    /// solved verdicts (the SubmitAnswer write path, #125). Verdicts map to
    /// questions by order of first appearance (problem1 → first question);
    /// questions past the last verdict get no update — no signal, no write.
    /// </summary>
    private async Task ApplyMasteryUpdatesAsync(
        string userId,
        IReadOnlyList<TranscriptTurn> turns,
        MockInterviewReviewDraft draft,
        CancellationToken cancellationToken)
    {
        var questionIds = turns
            .Where(t => t.QuestionId.HasValue)
            .Select(t => t.QuestionId!.Value)
            .Distinct()
            .ToList();
        if (questionIds.Count == 0)
            return;

        var byId = await _db.Questions
            .AsNoTracking()
            .Where(q => questionIds.Contains(q.Id))
            .ToDictionaryAsync(q => q.Id, cancellationToken);

        // problem1 < problem2 < ... ordinal sort; interviews cap at 3 problems
        // so single-digit keys are enough (ponytail: no numeric key parsing).
        var verdicts = draft.TechnicalReview
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => e.Value)
            .ToList();

        var updates = new List<(StudentMastery Mastery, float Previous)>();
        var touchedConcepts = new HashSet<Guid>();

        foreach (var questionId in questionIds)
        {
            if (!byId.TryGetValue(questionId, out var question))
                continue;
            if (!touchedConcepts.Add(question.ConceptId))
                continue;
            var verdictIndex = questionIds.IndexOf(questionId);
            if (verdictIndex >= verdicts.Count)
                break; // fewer verdicts than questions — the rest get no signal

            var isCorrect = verdicts[verdictIndex].Solved;
            var parameters = await _db.BktParameters
                .FirstOrDefaultAsync(p => p.ConceptId == question.ConceptId, cancellationToken)
                ?? new BktParameters { ConceptId = question.ConceptId };

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

            var previous = mastery.MasteryProbability;
            mastery.MasteryProbability = _bktEngine.UpdateMastery(previous, parameters, isCorrect);
            if (isCorrect)
                mastery.CorrectAttempts++;
            else
                mastery.IncorrectAttempts++;
            mastery.LastPracticedAt = DateTime.UtcNow;
            mastery.UpdatedAt = DateTime.UtcNow;

            updates.Add((mastery, previous));
        }

        if (updates.Count == 0)
            return;

        await _db.SaveChangesAsync(cancellationToken);
        foreach (var (mastery, previous) in updates)
            await _eventPublisher.PublishMasteryUpdatedAsync(mastery, previous, cancellationToken);
    }

    /// <summary>Deterministic fallback when the model is down — degrade, never 500.</summary>
    private static MockInterviewReviewDraft CannedReview(IReadOnlyList<TranscriptTurn> turns)
    {
        var problemCount = Math.Max(
            turns.Count(t => t.QuestionId.HasValue),
            1);

        var technicalReview = new Dictionary<string, ProblemReview>(StringComparer.Ordinal);
        for (var i = 1; i <= problemCount; i++)
        {
            technicalReview[$"problem{i}"] = new ProblemReview(
                Solved: false,
                Complexity: "Not reviewed",
                Feedback: "Automated review unavailable for this interview.");
        }

        return new MockInterviewReviewDraft(
            50f,
            technicalReview,
            "Retake the interview once the review service is available to get scored feedback.");
    }
}
