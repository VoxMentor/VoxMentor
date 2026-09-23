using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.KnowledgeGraph.GetPrerequisiteChain;

/// <summary>
/// Walks Prerequisites upward (ConceptId → RequiredConceptId) with a
/// recursive CTE, returning the transitive prerequisite set ordered by depth.
/// ponytail: raw SQL because EF cannot translate recursive CTEs.
/// </summary>
public class GetPrerequisiteChainHandler
    : IRequestHandler<GetPrerequisiteChainQuery, ApiResponse<IReadOnlyList<PrerequisiteChainItemDto>>>
{
    /// <summary>Max recursion depth; guards against accidental cycles.</summary>
    public const int MaxDepth = 50;

    private readonly IApplicationDbContext _db;

    /// <summary>Initializes the handler with the read-only context.</summary>
    public GetPrerequisiteChainHandler(IApplicationDbContext db) => _db = db;

    /// <exception cref="UnauthorizedAccessException">Never — chain is read-only and concept-scoped.</exception>
    public async Task<ApiResponse<IReadOnlyList<PrerequisiteChainItemDto>>> Handle(
        GetPrerequisiteChainQuery request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH RECURSIVE prereq_chain AS (
                SELECT p."ConceptId", p."RequiredConceptId", 1 AS depth
                FROM "Prerequisites" p
                WHERE p."ConceptId" = {0}
                UNION
                SELECT p."ConceptId", p."RequiredConceptId", pc.depth + 1
                FROM "Prerequisites" p
                JOIN prereq_chain pc ON p."ConceptId" = pc."RequiredConceptId"
                WHERE pc.depth < {1}
            )
            SELECT c."Id" AS ConceptId, c."Name", MIN(pc.depth) AS Depth
            FROM prereq_chain pc
            JOIN "Concepts" c ON c."Id" = pc."RequiredConceptId"
            GROUP BY c."Id", c."Name"
            ORDER BY Depth, c."Name"
            """;

        var items = await _db.SqlQueryRaw<PrerequisiteChainItemDto>(sql, request.ConceptId, MaxDepth)
            .ToListAsync(cancellationToken);

        return ApiResponse<IReadOnlyList<PrerequisiteChainItemDto>>.SuccessResult(
            items, "Prerequisite chain retrieved successfully.");
    }
}
