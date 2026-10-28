namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>One row of GET /api/v1/mock/history, per docs/API.md §6.</summary>
public record MockInterviewHistoryItemDto(
    Guid InterviewId,
    string Type,
    string Status,
    float? Score,
    DateTime Date);

/// <summary>GET /api/v1/mock/history response (score null until Completed).</summary>
public record GetMockInterviewHistoryResultDto(IReadOnlyList<MockInterviewHistoryItemDto> Interviews);
