using VoxMentor.Domain.Enums;
using VoxMentor.Domain.Interfaces;

namespace VoxMentor.Domain.Entities;

/// <summary>A single AI tutor Q&amp;A session row.</summary>
public class TutorSession : IUserOwned
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid? ConceptId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string? Answer { get; set; }
    public TutorSessionStatus Status { get; set; } = TutorSessionStatus.Pending;
    public int TotalTokens { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
