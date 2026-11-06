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
        var cacheKey = $"jd:{userId}:{request.JdId}";
        var cached = ApplicationCacheService.GetT<GetJdResultDto>(_cache, cacheKey);
        if (cached is not null)
            return ApiResponse<GetJdResultDto>.SuccessResult(cached, "Retrieved from cache.");

        var jd = await _db.JobDescriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JdId, cancellationToken);

        if (jd is null)
            throw new NotFoundException("No job description found for the user.");

        var skillWeights = await _db.JdSkillWeights
            .AsNoTracking()
            .Where(sw => sw.JobDescriptionId == jd.Id)
            .ToListAsync(cancellationToken);

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

        ApplicationCacheService.SetT(_cache, cacheKey, dto);
        return ApiResponse<GetJdResultDto>.SuccessResult(dto, "Job description retrieved successfully.");
    }
}
