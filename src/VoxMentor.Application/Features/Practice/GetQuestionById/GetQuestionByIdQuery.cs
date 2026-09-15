using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetQuestionById;

/// <summary>
/// Retrieves a single practice question by ID. Hidden test cases are excluded.
/// </summary>
/// <param name="Id">The question ID.</param>
public record GetQuestionByIdQuery(Guid Id) : IRequest<ApiResponse<QuestionDetailDto>>;
