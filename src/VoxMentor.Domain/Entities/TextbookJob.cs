using VoxMentor.Domain.Enums;

namespace VoxMentor.Domain.Entities;

/// <summary>A single textbook ingestion job row (upload → chunk → embed).</summary>
public class TextbookJob
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;

    /// <summary>Concept every chunk of this upload is mapped to, if provided.</summary>
    public Guid? ConceptId { get; set; }

    public TextbookJobStatus Status { get; set; } = TextbookJobStatus.Pending;
    public int TotalChunks { get; set; }
    public int ProcessedChunks { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
