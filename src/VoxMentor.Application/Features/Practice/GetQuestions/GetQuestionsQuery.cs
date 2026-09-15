using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetQuestions;

/// <summary>
/// Retrieves a paginated, filterable list of practice questions for students.
/// </summary>
/// <param name="ConceptId">Optional concept filter.</param>
/// <param name="Difficulty">Optional difficulty filter (1-10).</param>
/// <param name="Page">1-indexed page number.</param>
/// <param name="PageSize">Number of items per page (max 100).</param>
public record GetQuestionsQuery(
    Guid? ConceptId = null,
    int? Difficulty = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<ApiResponse<GetQuestionsResultDto>>;
