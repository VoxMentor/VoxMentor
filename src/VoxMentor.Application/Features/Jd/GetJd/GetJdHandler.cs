using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;
using Microsoft.Extensions.Caching.Distributed;

namespace VoxMentor.Application.Features.Jd.GetJd;

/// <summary>
/// Returns a parsed JD plus the current student's readiness score, gap
/// analysis, and week-by-week roadmap (#118). Read-only — no tracking.
/// Ownership is enforced by the IUserOwned global query filter, never by a
/// hand-written UserId predicate.
/// </summary>
public class GetJdHandler : IRequestHandler<GetJdQuery, ApiResponse<GetJdResultDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDistributedCache _cache;

    public GetJdHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IDistributedCache cache)
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

        // Per CodeRabbit review: cache only static JD data (company, role, skill weights,
        // concepts). Always recompute readiness/gaps/roadmap from CURRENT masteries so a
        // submission never returns a stale readiness score.
        var cacheKey = $"jd:{userId}:{request.JdId}";
        var cachedStatic = ApplicationCacheService.GetT<GetJdStaticDataDto>(_cache, cacheKey);

        JobDescription jd;
        List<JdSkillWeight> skillWeights;
        List<Concept> concepts;

        if (cachedStatic is not null)
        {
            // Cache hit: use cached static data
            jd = await _db.JobDescriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == request.JdId, cancellationToken);
            if (jd is null)
                throw new NotFoundException("No job description found for the user.");

            skillWeights = cachedStatic.SkillWeights;
            concepts = cachedStatic.Concepts;
        }
        else
        {
            // Cache miss: load full data
            jd = await _db.JobDescriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == request.JdId, cancellationToken);

            if (jd is null)
                throw new NotFoundException("No job description found for the user.");

            skillWeights = await _db.JdSkillWeights
                .AsNoTracking()
                .Where(sw => sw.JobDescriptionId == jd.Id)
                .ToListAsync(cancellationToken);

            concepts = await _db.Concepts
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Cache static data for future requests (best-effort, never blocks).
            // Only skill weights and concepts are cached; readiness is always recomputed.
            ApplicationCacheService.SetT(_cache, cacheKey, new GetJdStaticDataDto(
                skillWeights, concepts));
        }

        // Always load fresh masteries and recompute readiness — readiness depends on
        // writes (submissions) that must not be masked by cache.
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

// Helper DTO for caching only static JD components (not readiness-derived data).
public record GetJdStaticDataDto(
    List<JdSkillWeight> SkillWeights,
    List<Concept> Concepts);