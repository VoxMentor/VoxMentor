using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Tutor.AskTutor;

/// <summary>
/// Starts an AI tutor answer session. Answer generation happens asynchronously;
/// the client receives a sessionId immediately (202) and polls or streams for the answer.
/// </summary>
/// <param name="Question">Student question, required, max 2000 chars.</param>
/// <param name="ConceptId">Optional concept the question is about; must exist if provided.</param>
public record AskTutorCommand(
    string Question,
    Guid? ConceptId = null
) : IRequest<ApiResponse<AskTutorResultDto>>;
