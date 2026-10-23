namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>Accepted response for POST /api/v1/mock/start (202) per docs/API.md Â§6.</summary>
public record StartMockInterviewResultDto(
    Guid InterviewId,
    string Type,
    string Status,
    int Duration,
    DateTime StartedAt,
    string OpeningMessage);
