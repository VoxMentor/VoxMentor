using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetReadiness;

/// <summary>
/// Computes the JD-weighted readiness score by joining skill weights with
/// the student's BKT mastery per concept. Read-only — no tracking.
/// </summary>
public class GetReadinessHandler : IRequestHandler<GetReadinessQuery, ApiResponse<ReadinessDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetReadinessHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <exception cref="UnauthorizedAccessException">No authenticated user.</exception>
    /// <exception cref="NotFoundException">No JD found for the user.</exception>
    public async Task<ApiResponse<ReadinessDto>> Handle(
        GetReadinessQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User must be authenticated to view readiness.");

        var jd = await LoadJobDescriptionAsync(userId, request.JdId, cancellationToken);

        var skillWeights = await _db.JdSkillWeights
            .AsNoTracking()
            .Where(sw => sw.JobDescriptionId == jd.Id)
            .ToListAsync(cancellationToken);

        var concepts = await _db.Concepts
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var masteries = await _db.StudentMasteries
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .ToDictionaryAsync(m => m.ConceptId, cancellationToken);

        var conceptByName = concepts
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First());

        var breakdown = new List<SkillBreakdownDto>();
        float scoreSum = 0f;

        foreach (var sw in skillWeights)
        {
            if (!conceptByName.TryGetValue(sw.SkillName, out var concept))
                continue;

            masteries.TryGetValue(concept.Id, out var mastery);
            var masteryValue = mastery?.MasteryProbability ?? 0f;
            var contribution = masteryValue * sw.Weight;
            scoreSum += contribution;

            breakdown.Add(new SkillBreakdownDto
            {
                Topic = sw.SkillName,
                Mastery = masteryValue,
                JdWeight = sw.Weight,
                Contribution = contribution
            });
        }

        var gaps = breakdown
            .Where(b => b.Mastery < 1f)
            .Select(b => new SkillGapDto
            {
                Topic = b.Topic,
                Severity = b.JdWeight * (1f - b.Mastery),
                Recommendation = $"Practice {b.Topic} — {b.JdWeight * (1f - b.Mastery):P0} gap remaining."
            })
            .OrderByDescending(g => g.Severity)
            .ToList();

        var dto = new ReadinessDto
        {
            Score = skillWeights.Count == 0 ? 0f : scoreSum * 100f,
            Breakdown = breakdown,
            Gaps = gaps,
            EstimatedWeeksToReady = jd.EstimatedWeeks
        };

        return ApiResponse<ReadinessDto>.SuccessResult(dto, "Readiness score retrieved successfully.");
    }

    private async Task<VoxMentor.Domain.Entities.JobDescription> LoadJobDescriptionAsync(
        string userId, Guid? jdId, CancellationToken ct)
    {
        VoxMentor.Domain.Entities.JobDescription? jd;

        if (jdId.HasValue)
        {
            jd = await _db.JobDescriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jdId.Value && j.UserId == userId, ct);
        }
        else
        {
            jd = await _db.JobDescriptions
                .AsNoTracking()
                .Where(j => j.UserId == userId)
                .OrderByDescending(j => j.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        if (jd is null)
            throw new NotFoundException("No job description found for the user.");

        return jd;
    }
}
