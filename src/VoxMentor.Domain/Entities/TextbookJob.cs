using VoxMentor.Domain.Enums;

namespace VoxMentor.Domain.Entities;

/// <summary>A single textbook ingestion job row (upload → chunk → embed).</summary>
public class TextbookJob
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;

    /// <summary>Concept every chunk of this upload is mapped to, if provided.</summary>
    public Guid? ConceptId { get; set; }

    /// <summary>
    /// SHA-256 hex of the staged file. Key (with ConceptId) for idempotent
    /// retries: a re-upload of the same content reuses the existing job
    /// instead of queueing a duplicate (#104 CodeRabbit finding).
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    public TextbookJobStatus Status { get; set; } = TextbookJobStatus.Pending;
    public int TotalChunks { get; set; }
    public int ProcessedChunks { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Time the claim at <see cref="RunId"/> was set. A <see cref="Processing"/>
    /// row whose claim is older than the lock timeout is treated as abandoned
    /// and may be reclaimed by the next run (#104 CodeRabbit).
    /// </summary>
    public DateTime? ClaimedAt { get; set; }

    /// <summary>
    /// Per-run ownership token, set once when a worker claims the job
    /// (Pending or stale Processing -> Processing + RunId). Concurrency token
    /// so two overlapping Execute runs cannot both claim the same row
    /// (Hangfire's DisableConcurrentExecution is best-effort only). Only the
    /// claimant may publish Completed or mark the row Failed; any other run
    /// backs off, or reclaims once the claim is stale.
    /// </summary>
    public Guid? RunId { get; set; }
}
