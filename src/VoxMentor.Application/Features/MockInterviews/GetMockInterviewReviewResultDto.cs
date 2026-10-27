namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>GET /api/v1/mock/{interviewId}/review response, per docs/API.md §6.</summary>
public record GetMockInterviewReviewResultDto(
    Guid InterviewId,
    string Status,
    float Score,
    MockInterviewReview Review,
    DateTime StartedAt,
    DateTime? CompletedAt);
