using System.ComponentModel.DataAnnotations.Schema;
using Pgvector;

namespace VoxMentor.Domain.Entities;

/// <summary>A textbook excerpt with a 768-dim embedding for RAG retrieval.</summary>
public class TextbookChunk
{
    public Guid Id { get; set; }

    /// <summary>The ingestion job that produced this chunk.</summary>
    public Guid JobId { get; set; }

    /// <summary>Concept this chunk was mapped to at upload time, if any.</summary>
    public Guid? ConceptId { get; set; }

    /// <summary>The chunk text fed to the retriever.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Origin filename, surfaced as the citation source.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>768-dim embedding vector (nomic-embed-text, pgvector).</summary>
    [Column(TypeName = "vector(768)")]
    public Vector? Embedding { get; set; }
}
