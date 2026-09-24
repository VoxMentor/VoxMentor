namespace VoxMentor.Application.Features.Tutor.AskTutor;

/// <summary>Accepted-response payload for a newly created tutor session.</summary>
/// <param name="SessionId">Id to poll via GET /api/v1/tutor/sessions/{sessionId}.</param>
/// <param name="Message">Human-readable next-step hint.</param>
public record AskTutorResultDto(
    Guid SessionId,
    string Message
);
