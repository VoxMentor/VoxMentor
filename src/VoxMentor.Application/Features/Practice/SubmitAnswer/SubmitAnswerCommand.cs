using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.SubmitAnswer;

/// <summary>
/// Submits a graded answer for a practice question so mastery can be updated
/// for the question's concept.
/// </summary>
/// <param name="QuestionId">The answered question.</param>
/// <param name="IsCorrect">Whether the student's answer was correct.</param>
public record SubmitAnswerCommand(
    Guid QuestionId,
    bool IsCorrect
) : IRequest<ApiResponse<SubmitAnswerResultDto>>;
