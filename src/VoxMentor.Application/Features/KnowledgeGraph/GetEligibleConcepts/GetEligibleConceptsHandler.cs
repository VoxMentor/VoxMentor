using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Practice.GetMastery;

namespace VoxMentor.Application.Features.KnowledgeGraph.GetEligibleConcepts;

/// <summary>
/// Computes eligibility for the authenticated student: eligible = all
/// prerequisites mastered (vacuous if none) and self not mastered;
/// almost-eligible = exactly one unmet prerequisite and self not mastered.
/// ponytail: one raw SQL round-trip per bucket; InMemory can't run CTEs so tests use Sqlite.
/// </summary>
public class GetEligibleConceptsHandler
    : IRequestHandler<GetEligibleConceptsQuery, ApiResponse<EligibleConceptsDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    /// <summary>Initializes the handler with the context and current-user service.</summary>
    public GetEligibleConceptsHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <exception cref="UnauthorizedAccessException">No authenticated user.</exception>
    public async Task<ApiResponse<EligibleConceptsDto>> Handle(
        GetEligibleConceptsQuery request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            throw new UnauthorizedAccessException("User must be authenticated to view eligibility.");
        }

        var threshold = GetMasteryHandler.MasteredThreshold;

        const string eligibleSql = """
            WITH mastered AS (
                SELECT "ConceptId"
                FROM "StudentMasteries"
                WHERE "UserId" = {0} AND "MasteryProbability" >= {1}
            )
            SELECT c."Id" AS ConceptId, c."Name", c."Category", c."DifficultyLevel"
            FROM "Concepts" c
            WHERE c."Id" NOT IN (SELECT "ConceptId" FROM mastered)
              AND NOT EXISTS (
                  SELECT 1 FROM "Prerequisites" p
                  WHERE p."ConceptId" = c."Id"
                    AND p."RequiredConceptId" NOT IN (SELECT "ConceptId" FROM mastered)
              )
            ORDER BY c."Category", c."DifficultyLevel", c."Name"
            """;

        const string almostSql = """
            WITH mastered AS (
                SELECT "ConceptId"
                FROM "StudentMasteries"
                WHERE "UserId" = {0} AND "MasteryProbability" >= {1}
            ),
            unmet AS (
                SELECT p."ConceptId",
                       SUM(CASE WHEN p."RequiredConceptId" NOT IN (SELECT "ConceptId" FROM mastered)
                                THEN 1 ELSE 0 END) AS missing
                FROM "Prerequisites" p
                GROUP BY p."ConceptId"
            )
            SELECT c."Id" AS ConceptId, c."Name", c."Category", c."DifficultyLevel"
            FROM unmet u
            JOIN "Concepts" c ON c."Id" = u."ConceptId"
            WHERE u.missing = 1
              AND c."Id" NOT IN (SELECT "ConceptId" FROM mastered)
            ORDER BY c."Category", c."DifficultyLevel", c."Name"
            """;

        var eligible = await _db.SqlQueryRaw<EligibleConceptDto>(eligibleSql, userId, threshold)
            .ToListAsync(cancellationToken);
        var almost = await _db.SqlQueryRaw<EligibleConceptDto>(almostSql, userId, threshold)
            .ToListAsync(cancellationToken);

        var dto = new EligibleConceptsDto { Eligible = eligible, AlmostEligible = almost };
        return ApiResponse<EligibleConceptsDto>.SuccessResult(dto, "Eligibility retrieved successfully.");
    }
}
