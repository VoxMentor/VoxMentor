using VoxMentor.Domain.Enums;
using VoxMentor.Domain.Interfaces;

namespace VoxMentor.Domain.Entities;

/// <summary>A single AI tutor Q&amp;A session row.</summary>
public class TutorSession : IUserOwned
{
    /// <summary>Primary key of the session.</summary>
    public Guid Id { get; set; }

    /// <summary>Student who asked the question (backs the global user filter).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Optional concept the question is scoped to.</summary>
    public Guid? ConceptId { get; set; }

    /// <summary>The student's question (max 2000 chars).</summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>Generated answer; set once generation completes or fails.</summary>
    public string? Answer { get; set; }

    /// <summary>
    /// Lifecycle status; also the concurrency token for claims and terminal
    /// writes (#72): Pending → Streaming → Completed/Failed.
    /// </summary>
    public TutorSessionStatus Status { get; set; } = TutorSessionStatus.Pending;

    /// <summary>Truncated failure message (max 1000 chars) when Status is Failed.</summary>
    public string? Error { get; set; }

    /// <summary>Eval token count from Ollama's final stream chunk.</summary>
    public int TotalTokens { get; set; }

    /// <summary>Row creation time (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When a generator claimed the row and started streaming (UTC).</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Last enqueue time (UTC) — the reconciliation sweep's dedup claim.</summary>
    public DateTime? QueuedAt { get; set; }

    /// <summary>When the session reached a terminal status (UTC).</summary>
    public DateTime? CompletedAt { get; set; }
}
