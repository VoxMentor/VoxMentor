using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Admin.GetQuestions;

/// <summary>
/// Retrieves a paginated list of questions for admin curation.
/// </summary>
/// <param name="Page">1-indexed page number.</param>
/// <param name="PageSize">Number of items per page (max 100).</param>
public record GetQuestionsQuery(int Page = 1, int PageSize = 20) : IRequest<ApiResponse<GetQuestionsResultDto>>;
