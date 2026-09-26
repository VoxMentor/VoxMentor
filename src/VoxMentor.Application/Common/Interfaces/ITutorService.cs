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
/// </summary>
public sealed record TutorChunk(string Text, bool IsFinal = false, int? EvalCount = null);
