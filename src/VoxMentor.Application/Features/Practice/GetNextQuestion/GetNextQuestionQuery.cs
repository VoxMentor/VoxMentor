using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetNextQuestion;

/// <summary>
/// Selects the next adaptive question for the student. Targets the weakest
/// concept at difficulty 1 + mastery×9.
/// </summary>
public record GetNextQuestionQuery : IRequest<ApiResponse<NextQuestionDto>>;
