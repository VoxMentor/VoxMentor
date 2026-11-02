namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>
/// Response for POST /api/v1/mock/{interviewId}/answer per docs/API.md §6.
/// QuestionId is the problem the interview is now on: the newly introduced
/// problem after an advance, the same problem after a probe, null once the
/// final problem was answered (#148).
/// </summary>
public record AnswerMockInterviewResultDto(
    string InterviewerMessage,
    int TurnNumber,
    bool InterviewOngoing,
    Guid? QuestionId = null);
