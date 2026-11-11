namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Streams a RAG answer for the AI tutor: retrieve textbook chunks, build the
/// prompt, and yield Ollama output tokens as they are generated.
/// </summary>
public interface ITutorService
{
    /// <summary>
    /// Retrieves context for <paramref name="question"/> (optionally concept-filtered)
    /// and streams generated tokens. The final chunk carries the token count.
    /// </summary>
    IAsyncEnumerable<TutorChunk> StreamAnswerAsync(
        string question,
        Guid? conceptId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One streamed unit from the tutor pipeline. <see cref="EvalCount"/> is set on
/// the final chunk only (Ollama's <c>eval_count</c>), <see cref="IsFinal"/> marks it.
/// <see cref="Citations"/> carries the retrieved source chunks on the final chunk
/// so the client can render citation material (#172).
/// </summary>
public sealed record TutorChunk(string Text, bool IsFinal = false, int? EvalCount = null, Citation[]? Citations = null);

/// <summary>One retrieved source chunk surfaced as a citation in the tutor UI.</summary>
public sealed record Citation(string Source, string ChunkText, double Relevance);
