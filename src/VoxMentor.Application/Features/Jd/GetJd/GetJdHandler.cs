using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Features.Jd.GetJd;

/// <summary>
/// Returns a parsed JD plus the current student's readiness score, gap
/// analysis, and week-by-week roadmap (#118). Read-only — no tracking.
/// Ownership is enforced by the IUserOwned global query filter, never by a
/// hand-written UserId predicate.
/// </summary>
public class GetJdHandler : IRequestHandler<GetJdQuery, ApiResponse<GetJdResultDto>>
{
    /// <summary>Cached JD data (the JD row plus its skill weights). Readiness is computed fresh per request.</summary>
    /// <remarks>
    /// The snapshot stores primitive DTO fields, not EF Core entities, so the cache
    /// contract is decoupled from the persistence model (M2).
    /// Staleness: a deleted/edited JD is served from cache until the 300s TTL expires
    /// (M3). Invalidation on JD write would be needed to tighten this window.
    /// </remarks>
    private sealed record JdData(Guid Id, string CompanyName, string Role, string Difficulty, int EstimatedWeeks, DateTime CreatedAt);
    private sealed record JdSnapshot(JdData Jd, List<SkillWeightData> SkillWeights);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDistributedCache _cache;

    public GetJdHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDistributedCache cache)
    {
        _db = db;
        _currentUser = currentUser;
        _cache = cache;
    }

    /// <exception cref="UnauthorizedAccessException">No authenticated user.</exception>
    /// <exception cref="NotFoundException">JD unknown or not owned by the caller.</exception>
    public async Task<ApiResponse<GetJdResultDto>> Handle(
        GetJdQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User must be authenticated to view a JD.");

        // Cache the raw JD data separately from the derived readiness results so a
        // mastery change never serves a stale readiness score — readiness is
        // recomputed from the student's current mastery on every request.
        // Versioned key prevents a stale write from an in-flight read from
        // repopulating the cache after invalidation bumps the version.
        var cacheKey = await ApplicationCacheService.VersionedKeyAsync(_cache, userId, "jd", request.JdId.ToString(), cancellationToken);
        var snapshot = await ApplicationCacheService.GetTAsync<JdSnapshot>(_cache, cacheKey, cancellationToken);
        JdData? jd = null;
        List<SkillWeightData>? skillWeights = null;
        if (snapshot is not null)
        {
            jd = snapshot.Jd;
            skillWeights = snapshot.SkillWeights;
        }
        else
        {
            // Foreign and unknown JDs both surface as null → 404 (no existence oracle).
            var jdEntity = await _db.JobDescriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == request.JdId, cancellationToken);

            if (jdEntity is null)
                throw new NotFoundException("No job description found for the user.");

            skillWeights = await _db.JdSkillWeights
                .AsNoTracking()
                .Where(sw => sw.JobDescriptionId == jdEntity.Id)
                .Select(sw => new SkillWeightData(sw.SkillName, sw.Weight, sw.IsTechnical))
                .ToListAsync(cancellationToken);

            jd = new JdData(jdEntity.Id, jdEntity.CompanyName, jdEntity.Role, jdEntity.Difficulty, jdEntity.EstimatedWeeks, jdEntity.CreatedAt);
            await ApplicationCacheService.SetTAsync(_cache, cacheKey, new JdSnapshot(jd, skillWeights), cancellationToken);
        }

        var concepts = await _db.Concepts
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var masteries = await _db.StudentMasteries
            .AsNoTracking()
            .ToDictionaryAsync(m => m.ConceptId, cancellationToken);

        var readiness = ReadinessCalculator.Compute(
            skillWeights, concepts, masteries, jd.EstimatedWeeks);

        var dto = new GetJdResultDto
        {
            JdId = jd.Id,
            CompanyName = jd.CompanyName,
            Role = jd.Role,
            TechnicalSkills = skillWeights
                .Where(sw => sw.IsTechnical)
                .ToDictionary(sw => sw.SkillName, sw => sw.Weight, StringComparer.Ordinal),
            HrSkills = skillWeights
                .Where(sw => !sw.IsTechnical)
                .ToDictionary(sw => sw.SkillName, sw => sw.Weight, StringComparer.Ordinal),
            Difficulty = jd.Difficulty,
            ReadinessScore = readiness.Score,
            Gaps = readiness.Gaps,
            EstimatedWeeks = jd.EstimatedWeeks,
            Roadmap = RoadmapBuilder.Build(readiness.Gaps, concepts, jd.EstimatedWeeks),
            CreatedAt = jd.CreatedAt
        };

        return ApiResponse<GetJdResultDto>.SuccessResult(dto, "Job description retrieved successfully.");
    }
}
