using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Admin.GetQuestions;

/// <summary>
/// Retrieves a paginated list of questions for admin curation.
/// Bound from the query string as a class (flat keys: page, pageSize).
/// </summary>
public class GetQuestionsQuery : IRequest<ApiResponse<GetQuestionsResultDto>>
{
    /// <summary>1-indexed page number.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Number of items per page (max 100).</summary>
    public int PageSize { get; set; } = 20;
}
