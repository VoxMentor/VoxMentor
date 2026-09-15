using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetNextQuestion;

/// <summary>
/// Adaptive next-question selector. Picks the weakest concept (lowest BKT
/// mastery), targets difficulty 1 + mastery×9, and returns the closest
/// unanswered question.
/// </summary>
public class GetNextQuestionHandler : IRequestHandler<GetNextQuestionQuery, ApiResponse<NextQuestionDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetNextQuestionHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
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

        // Find weakest concept: lowest mastery (unpracticed = 0.1 default prior)
        var weakestConcept = concepts
            .OrderBy(c => masteryByConcept.TryGetValue(c.Id, out var m) ? m.MasteryProbability : 0.1f)
            .First();

        var mastery = masteryByConcept.TryGetValue(weakestConcept.Id, out var m2) ? m2.MasteryProbability : 0.1f;

        // Target difficulty: 1 + mastery × 9, clamped 1-10
        var targetDifficulty = Math.Clamp((int)Math.Round(1 + mastery * 9), 1, 10);

        // Get questions for the weakest concept, excluding already-attempted
        var attemptedIds = new HashSet<Guid>(await _db.CodeSubmissions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.QuestionId)
            .ToListAsync(cancellationToken));

        var candidate = await _db.Questions
            .AsNoTracking()
            .Where(q => q.ConceptId == weakestConcept.Id && !attemptedIds.Contains(q.Id))
            .OrderBy(q => Math.Abs(q.Difficulty - targetDifficulty))
            .ThenBy(q => q.Title)
            .FirstOrDefaultAsync(cancellationToken);

        // Fallback: if all questions attempted for this concept, try any unanswered question
        candidate ??= await _db.Questions
            .AsNoTracking()
            .Where(q => !attemptedIds.Contains(q.Id))
            .OrderBy(q => Math.Abs(q.Difficulty - targetDifficulty))
            .ThenBy(q => q.Title)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
            throw new NotFoundException("No unanswered questions available.");

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
}
