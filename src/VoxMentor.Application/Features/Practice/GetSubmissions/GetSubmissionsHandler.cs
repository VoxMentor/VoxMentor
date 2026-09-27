using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetSubmissions;

/// <summary>
/// Returns the student's most recent code submissions with question title,
/// concept name, pass/fail, mastery delta, and timestamp.
/// </summary>
public class GetSubmissionsHandler : IRequestHandler<GetSubmissionsQuery, ApiResponse<IReadOnlyList<SubmissionItemDto>>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetSubmissionsHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<IReadOnlyList<SubmissionItemDto>>> Handle(
        GetSubmissionsQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User must be authenticated to list submissions.");

        var limit = Math.Clamp(request.Limit, 1, 50);

        var submissions = await _db.CodeSubmissions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(limit)
            .Select(s => new { s.Id, s.QuestionId, s.IsCorrect, s.MasteryBefore, s.MasteryAfter, s.CreatedAt })
            .ToListAsync(cancellationToken);

        if (submissions.Count == 0)
            return ApiResponse<IReadOnlyList<SubmissionItemDto>>.SuccessResult(Array.Empty<SubmissionItemDto>());

        // ponytail: no navigation properties CodeSubmission->Question->Concept, so 2 extra keyed lookups instead of joins
        var questionIds = submissions.Select(s => s.QuestionId).Distinct().ToList();
        var questions = await _db.Questions
            .AsNoTracking()
            .Where(q => questionIds.Contains(q.Id))
            .Select(q => new { q.Id, q.Title, q.ConceptId })
            .ToListAsync(cancellationToken);

        var conceptIds = questions.Select(q => q.ConceptId).Distinct().ToList();
        var concepts = await _db.Concepts
            .AsNoTracking()
            .Where(c => conceptIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);

        var titleByQuestion = questions.ToDictionary(q => q.Id, q => (q.Title, q.ConceptId));
        var nameByConcept = concepts.ToDictionary(c => c.Id, c => c.Name);

        var items = submissions.Select(s =>
        {
            titleByQuestion.TryGetValue(s.QuestionId, out var q);
            var conceptName = nameByConcept.TryGetValue(q.ConceptId, out var n) ? n : string.Empty;
            float? delta = s.MasteryBefore.HasValue && s.MasteryAfter.HasValue
                ? s.MasteryAfter.Value - s.MasteryBefore.Value
                : null;
            return new SubmissionItemDto(s.Id, q.Title ?? string.Empty, conceptName, s.IsCorrect, delta, s.CreatedAt);
        }).ToList();

        return ApiResponse<IReadOnlyList<SubmissionItemDto>>.SuccessResult(items);
    }
}
