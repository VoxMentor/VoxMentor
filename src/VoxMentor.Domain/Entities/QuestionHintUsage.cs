using VoxMentor.Domain.Interfaces;

namespace VoxMentor.Domain.Entities;

/// <summary>
/// Tracks whether a student used a hint on a practice question attempt.
/// One row per (user, question, attempt) triple; <see cref="UsedHint"/> is
/// true when the student requested at least one hint. Used by the difficulty
/// calibration scorer (issue #128, factor 3).
/// </summary>
public class QuestionHintUsage : IUserOwned
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid QuestionId { get; set; }
    public Guid AttemptId { get; set; }
    public bool UsedHint { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
