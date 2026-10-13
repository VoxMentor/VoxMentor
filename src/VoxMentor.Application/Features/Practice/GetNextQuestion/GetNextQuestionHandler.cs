using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Features.Practice.GetNextQuestion;

/// <summary>
/// Adaptive next-question selector. Targets difficulty 1 + mastery×9 and
/// returns the closest unanswered question. Concept choice (#119): with no JD
/// it picks the weakest concept by raw BKT mastery; with a JD it ranks by
/// severity = jdWeight × (1 − mastery) and takes a ε=0.2 exploratory pick
/// instead of the top-ranked one.
/// </summary>
public class GetNextQuestionHandler : IRequestHandler<GetNextQuestionQuery, ApiResponse<NextQuestionDto>>
{
    /// <summary>Exploration rate of the JD-weighted selector: chance of ignoring the top-ranked concept.</summary>
    private const double Epsilon = 0.2;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly Random _random;

    public GetNextQuestionHandler(IApplicationDbContext db, ICurrentUserService currentUser, Random random)
    {
        _db = db;
        _currentUser = currentUser;
        _random = random;
    }

    public async Task<ApiResponse<NextQuestionDto>> Handle(
        GetNextQuestionQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User must be authenticated to get the next question.");

        var concepts = await _db.Concepts
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (concepts.Count == 0)
            throw new NotFoundException("No concepts available.");

        var masteries = await _db.StudentMasteries
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);

        var masteryByConcept = masteries.ToDictionary(m => m.ConceptId);

        // Loaded before concept selection: the JD-weighted selector needs it to
        // keep exploration on concepts that still have an unanswered question.
        var attemptedIds = new HashSet<Guid>(await _db.CodeSubmissions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.QuestionId)
            .ToListAsync(cancellationToken));

        // Find the concept: pin to the requested one, otherwise JD-weighted
        // ε-greedy when the user has a JD, otherwise weakest by raw mastery.
        var selectedConcept = request.ConceptId is { } requestedId
            ? concepts.FirstOrDefault(c => c.Id == requestedId)
                ?? throw new NotFoundException("Concept not found.")
            : await SelectConceptAsync(concepts, masteryByConcept, attemptedIds, cancellationToken);

        var mastery = masteryByConcept.TryGetValue(selectedConcept.Id, out var m2) ? m2.MasteryProbability : 0.1f;

        // Target difficulty: 1 + mastery × 9, clamped 1-10
        var targetDifficulty = Math.Clamp((int)Math.Round(1 + mastery * 9), 1, 10);

        var candidate = await _db.Questions
            .AsNoTracking()
            .Where(q => q.ConceptId == selectedConcept.Id && !attemptedIds.Contains(q.Id))
            .OrderBy(q => Math.Abs(q.Difficulty - targetDifficulty))
            .ThenBy(q => q.Title)
            .FirstOrDefaultAsync(cancellationToken);

        if (request.ConceptId is not null)
        {
            // Filtered mode never leaves the concept: retry an attempted question
            // rather than falling back to a different concept.
            candidate ??= await _db.Questions
                .AsNoTracking()
                .Where(q => q.ConceptId == selectedConcept.Id)
                .OrderBy(q => Math.Abs(q.Difficulty - targetDifficulty))
                .ThenBy(q => q.Title)
                .FirstOrDefaultAsync(cancellationToken);

            if (candidate is null)
                throw new NotFoundException("No questions available for this concept.");
        }
        else
        {
            // Fallback: if all questions attempted for this concept, try any unanswered question
            candidate ??= await _db.Questions
                .AsNoTracking()
                .Where(q => !attemptedIds.Contains(q.Id))
                .OrderBy(q => Math.Abs(q.Difficulty - targetDifficulty))
                .ThenBy(q => q.Title)
                .FirstOrDefaultAsync(cancellationToken);

            if (candidate is null)
                throw new NotFoundException("No unanswered questions available.");
        }

        var conceptName = concepts.First(c => c.Id == candidate.ConceptId).Name;

        var visibleTestCases = candidate.HiddenTestCaseCount > 0
            ? candidate.TestCases[..^candidate.HiddenTestCaseCount]
            : candidate.TestCases;

        var dto = new NextQuestionDto(
            candidate.Id,
            candidate.ConceptId,
            conceptName,
            candidate.Title,
            candidate.Description,
            candidate.QuestionType,
            candidate.Difficulty,
            candidate.ExampleInputs,
            candidate.ExampleOutputs,
            candidate.StarterCode,
            visibleTestCases,
            candidate.Rubric);

        return ApiResponse<NextQuestionDto>.SuccessResult(dto);
    }

    /// <summary>
    /// Picks the concept to target when no explicit ConceptId was requested.
    /// No JD → weakest by raw mastery, exactly as before #119 (no <see cref="Random"/> touched).
    /// With a JD → severity = jdWeight × (1 − mastery), top-ranked unless the ε roll explores.
    /// </summary>
    /// <param name="concepts">All concepts, in query order.</param>
    /// <param name="masteryByConcept">User's mastery rows keyed by concept id.</param>
    /// <param name="attemptedIds">Questions already submitted; narrows the exploration pool.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<Concept> SelectConceptAsync(
        IReadOnlyList<Concept> concepts,
        IReadOnlyDictionary<Guid, StudentMastery> masteryByConcept,
        IReadOnlySet<Guid> attemptedIds,
        CancellationToken ct)
    {
        float MasteryOf(Guid conceptId)
            => masteryByConcept.TryGetValue(conceptId, out var row) ? row.MasteryProbability : 0.1f;

        // IUserOwned global filter scopes this to the caller's JDs — no UserId
        // predicate. Latest JD wins, matching GetReadinessHandler's fallback.
        var jd = await _db.JobDescriptions
            .AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (jd is null)
        {
            return concepts
                .OrderBy(c => MasteryOf(c.Id))
                .First();
        }

        var weights = await _db.JdSkillWeights
            .AsNoTracking()
            .Where(sw => sw.JobDescriptionId == jd.Id)
            .ToListAsync(ct);

        // First-wins by name, same rule as ReadinessCalculator: Concept.Name has
        // no unique index, so score and selector must resolve it identically.
        var weightByName = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var sw in weights)
            weightByName.TryAdd(sw.SkillName, sw.Weight);

        float Severity(Concept c)
            => weightByName.GetValueOrDefault(c.Name) * (1f - MasteryOf(c.Id));

        // Exploration must not waste its roll on a concept with nothing left to
        // answer — that would fall through to the "any unanswered" fallback and
        // serve an unrelated topic instead of the one the roll picked.
        var eligibleIds = new HashSet<Guid>(await _db.Questions
            .AsNoTracking()
            .Where(q => !attemptedIds.Contains(q.Id))
            .Select(q => q.ConceptId)
            .Distinct()
            .ToListAsync(ct));

        var eligible = concepts.Where(c => eligibleIds.Contains(c.Id)).ToList();

        if (eligible.Count > 0 && _random.NextDouble() < Epsilon)
            return eligible[_random.Next(eligible.Count)];

        // Rank only concepts that can still serve a question: otherwise the top
        // pick would fall through to the "any unanswered" fallback and the
        // difficulty target would be computed for a topic the student never
        // saw. An empty pool (every question attempted) keeps the old
        // behaviour — rank everything and let the caller's fallback 404.
        var pool = eligible.Count > 0 ? eligible : concepts;

        // Ties (e.g. a JD with no extracted weights, where every severity is 0)
        // fall back to weakest mastery, so behaviour degrades to the pre-#119
        // selector instead of freezing on the first concept in query order.
        return pool
            .OrderByDescending(Severity)
            .ThenBy(c => MasteryOf(c.Id))
            .First();
    }
}
