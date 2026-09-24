using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetQuestions;

/// <summary>
/// Retrieves a paginated, filterable list of practice questions for students.
/// Bound from the query string as a class (flat keys: conceptId, difficulty, page, pageSize).
/// </summary>
public class GetQuestionsQuery : IRequest<ApiResponse<GetQuestionsResultDto>>
{
    /// <summary>Optional concept filter.</summary>
    public Guid? ConceptId { get; set; }

    /// <summary>Optional difficulty filter (1-10).</summary>
    public int? Difficulty { get; set; }

    /// <summary>1-indexed page number.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Number of items per page (max 100).</summary>
    public int PageSize { get; set; } = 20;
}
