using VoxMentor.Domain.Enums;

namespace VoxMentor.Domain.Entities;

public class CodeSubmission
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid QuestionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int? ExecutionTimeMs { get; set; }
    public float? PlagiarismScore { get; set; }
    public string? AiEvaluation { get; set; }
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
