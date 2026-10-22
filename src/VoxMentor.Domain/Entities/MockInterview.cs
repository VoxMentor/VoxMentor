using VoxMentor.Domain.Enums;
using VoxMentor.Domain.Interfaces;

namespace VoxMentor.Domain.Entities;

/// <summary>A mock-interview run, optionally scoped to a job description (#123).</summary>
public class MockInterview : IUserOwned
{
    /// <summary>Primary key of the interview.</summary>
    public Guid Id { get; set; }

    /// <summary>Student who started the interview (backs the global user filter).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Interview format from the <see cref="InterviewType"/> enum (int column).</summary>
    public InterviewType Type { get; set; }

    /// <summary>Total problems planned for the run (up to 3 per API.md §6).</summary>
    public int TotalQuestions { get; set; }

    /// <summary>Problems answered so far.</summary>
    public int AnsweredQuestions { get; set; }

    /// <summary>AI review score (0–100); 0 until the interview completes.</summary>
    public float Score { get; set; }

    /// <summary>Human-readable review summary; the structured payload lives in ReviewJson.</summary>
    public string? Feedback { get; set; }

    /// <summary>When the interview started (UTC).</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the interview reached a terminal status (UTC).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Optional job description this interview was scoped to (nullable FK, SetNull on delete).</summary>
    public Guid? JdId { get; set; }

    /// <summary>Lifecycle status: Pending → InProgress → Completed/Failed (#123).</summary>
    public MockInterviewStatus Status { get; set; } = MockInterviewStatus.Pending;

    /// <summary>JSON array of ordered conversation turns (candidate/interviewer messages).</summary>
    public string? TranscriptJson { get; set; }

    /// <summary>Structured AI review payload (score, per-problem feedback, next steps).</summary>
    public string? ReviewJson { get; set; }
}
