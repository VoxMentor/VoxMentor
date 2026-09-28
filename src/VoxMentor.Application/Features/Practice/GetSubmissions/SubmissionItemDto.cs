namespace VoxMentor.Application.Features.Practice.GetSubmissions;

/// <summary>A single recent submission row for the dashboard activity feed.</summary>
public record SubmissionItemDto(
    Guid SubmissionId,
    string QuestionTitle,
    string ConceptName,
    bool IsCorrect,
    float? MasteryDelta,
    DateTime CreatedAt);
