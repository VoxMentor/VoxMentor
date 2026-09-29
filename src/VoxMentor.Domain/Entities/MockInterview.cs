using VoxMentor.Domain.Enums;
using VoxMentor.Domain.Interfaces;

namespace VoxMentor.Domain.Entities;

public class MockInterview : IUserOwned
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public InterviewType Type { get; set; }
    public int TotalQuestions { get; set; }
    public int AnsweredQuestions { get; set; }
    public float Score { get; set; }
    public string? Feedback { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
