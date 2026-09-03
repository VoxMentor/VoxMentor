using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.SubmitAnswer;

public record SubmitAnswerCommand(
    Guid QuestionId,
    bool IsCorrect
) : IRequest<ApiResponse<SubmitAnswerResultDto>>;
