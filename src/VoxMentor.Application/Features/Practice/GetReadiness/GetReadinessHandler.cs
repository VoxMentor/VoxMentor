using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Services;

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

        var dto = ReadinessCalculator.Compute(
            skillWeights, concepts, masteries, jd.EstimatedWeeks);

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
