using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetNextQuestion;

/// <summary>
/// Selects the next adaptive question for the student. Without jdId, targets the
/// weakest concept at difficulty 1 + mastery×9. With jdId, weights by JD skill
/// priorities (when JD table exists).
/// </summary>
/// <param name="JdId">Optional job description ID for JD-weighted selection.</param>
public record GetNextQuestionQuery(Guid? JdId = null) : IRequest<ApiResponse<NextQuestionDto>>;
