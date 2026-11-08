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

        // Foreign and unknown JDs both surface as null → 404 (no existence oracle).
        var jd = await _db.JobDescriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JdId, cancellationToken);

        if (jd is null)
            throw new NotFoundException("No job description found for the user.");

        var cacheKey = $"jd:{userId}:{request.JdId}";
        var cachedStatic = ApplicationCacheService.GetT<GetJdStaticDataDto>(_cache, cacheKey);

        string companyName;
        string role;
        string difficulty;
        int estimatedWeeks;
        DateTime createdAt;
        List<SkillWeightData> skillWeights;
        List<Concept> concepts;

        if (cachedStatic is not null)
        {
            companyName = cachedStatic.CompanyName;
            role = cachedStatic.Role;
            difficulty = cachedStatic.Difficulty;
            estimatedWeeks = cachedStatic.EstimatedWeeks;
            createdAt = cachedStatic.CreatedAt;
            skillWeights = cachedStatic.SkillWeights;
            concepts = cachedStatic.Concepts;
        }
        else
        {
            companyName = jd.CompanyName;
            role = jd.Role;
            difficulty = jd.Difficulty;
            estimatedWeeks = jd.EstimatedWeeks;
            createdAt = jd.CreatedAt;

            skillWeights = await _db.JdSkillWeights
                .AsNoTracking()
                .Where(sw => sw.JobDescriptionId == jd.Id)
                .Select(sw => new SkillWeightData(sw.SkillName, sw.Weight, sw.IsTechnical))
                .ToListAsync(cancellationToken);

            concepts = await _db.Concepts
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Cache only static JD data (JD fields, skill weights, concepts) — best-effort
            ApplicationCacheService.SetT(_cache, cacheKey, new GetJdStaticDataDto(
                jd.Id, jd.CompanyName, jd.Role, jd.Difficulty, jd.EstimatedWeeks, jd.CreatedAt,
                skillWeights, concepts));
        }

        // Always load fresh masteries and recompute readiness — never stale
        var masteries = await _db.StudentMasteries
            .AsNoTracking()
            .ToDictionaryAsync(m => m.ConceptId, cancellationToken);

        var readiness = ReadinessCalculator.Compute(
            skillWeights, concepts, masteries, estimatedWeeks);

        var dto = new GetJdResultDto
        {
            JdId = jd.Id,
            CompanyName = companyName,
            Role = role,
            TechnicalSkills = skillWeights
                .Where(sw => sw.IsTechnical)
                .ToDictionary(sw => sw.SkillName, sw => sw.Weight, StringComparer.Ordinal),
            HrSkills = skillWeights
                .Where(sw => !sw.IsTechnical)
                .ToDictionary(sw => sw.SkillName, sw => sw.Weight, StringComparer.Ordinal),
            Difficulty = difficulty,
            ReadinessScore = readiness.Score,
            Gaps = readiness.Gaps,
            EstimatedWeeks = estimatedWeeks,
            Roadmap = RoadmapBuilder.Build(readiness.Gaps, concepts, estimatedWeeks),
            CreatedAt = createdAt
        };

        return ApiResponse<GetJdResultDto>.SuccessResult(dto, "Job description retrieved successfully.");
    }
}

// Helper DTO for caching only static JD components (not readiness-derived data).
public record GetJdStaticDataDto(
    Guid Id,
    string CompanyName,
    string Role,
    string Difficulty,
    int EstimatedWeeks,
    DateTime CreatedAt,
    List<SkillWeightData> SkillWeights,
    List<Concept> Concepts);