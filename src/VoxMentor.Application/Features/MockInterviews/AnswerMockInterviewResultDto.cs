namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>Response for POST /api/v1/mock/{interviewId}/answer per docs/API.md §6.</summary>
public record AnswerMockInterviewResultDto(
    string InterviewerMessage,
    int TurnNumber,
    bool InterviewOngoing);
