using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.SubmitCode;

/// <summary>Submits code for a practice question: executes test cases, evaluates with AI, and updates mastery.</summary>
/// <param name="QuestionId">The question the code solves.</param>
/// <param name="Code">The submitted source code.</param>
/// <param name="Language">Submission language: python, java, cpp, javascript, or csharp.</param>
public record SubmitCodeCommand(
    Guid QuestionId,
    string Code,
    string Language) : IRequest<ApiResponse<SubmitCodeResultDto>>;
