using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.Tutor.GetTutorSession;

/// <summary>Poll response for a tutor session.</summary>
public record TutorSessionDto(
    Guid SessionId,
    Guid? ConceptId,
    string Question,
    string? Answer,
    TutorSessionStatus Status,
    int TotalTokens,
    DateTime CreatedAt,
    DateTime? CompletedAt
);
