using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetMastery;

/// <summary>
/// Builds the authenticated student's mastery profile by left-joining every
/// concept with the student's mastery row (if any). Read-only: no tracking,
/// no retries — there is nothing to race on.
/// </summary>
public class GetMasteryHandler : IRequestHandler<GetMasteryQuery, ApiResponse<MasteryProfileDto>>
{
    /// <summary>Mastery probability at or above which a concept counts as mastered.</summary>
    public const float MasteredThreshold = 0.85f;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetMasteryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <exception cref="UnauthorizedAccessException">No authenticated user.</exception>
    public async Task<ApiResponse<MasteryProfileDto>> Handle(GetMasteryQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            throw new UnauthorizedAccessException("User must be authenticated to view mastery.");
        }

        var concepts = await _db.Concepts
            .AsNoTracking()
            .OrderBy(c => c.Category)
            .ThenBy(c => c.DifficultyLevel)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var masteries = await _db.StudentMasteries
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .ToDictionaryAsync(m => m.ConceptId, cancellationToken);

        var profile = new MasteryProfileDto { TotalConcepts = concepts.Count };

        foreach (var concept in concepts)
        {
            masteries.TryGetValue(concept.Id, out var mastery);

            var probability = mastery?.MasteryProbability;
            profile.Concepts.Add(new MasteryConceptDto
            {
                ConceptId = concept.Id,
                Name = concept.Name,
                Category = concept.Category,
                DifficultyLevel = concept.DifficultyLevel,
                MasteryProbability = probability,
                CorrectAttempts = mastery?.CorrectAttempts ?? 0,
                IncorrectAttempts = mastery?.IncorrectAttempts ?? 0,
                IsMastered = probability is >= MasteredThreshold,
                LastPracticedAt = mastery?.LastPracticedAt
            });

            if (probability is >= MasteredThreshold)
            {
                profile.MasteredCount++;
            }
        }

        profile.OverallReadiness = concepts.Count == 0
            ? 0f
            : profile.Concepts.Sum(c => c.MasteryProbability ?? 0f) / profile.TotalConcepts;

        return ApiResponse<MasteryProfileDto>.SuccessResult(profile, "Mastery profile retrieved successfully.");
    }
}
