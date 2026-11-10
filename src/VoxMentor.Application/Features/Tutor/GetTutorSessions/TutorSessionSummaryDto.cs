using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.Tutor.GetTutorSessions;

/// <summary>Summary row for a tutor session in the student's session list.</summary>
public record TutorSessionSummaryDto(
    Guid SessionId,
    Guid? ConceptId,
    string Question,
    TutorSessionStatus Status,
    int TotalTokens,
    DateTime CreatedAt,
    DateTime? CompletedAt);
