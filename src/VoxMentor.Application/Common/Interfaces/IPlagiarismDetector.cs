namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Result of a plagiarism detection analysis.
/// </summary>
/// <param name="Score">Cosine similarity score from 0.0 (original) to 1.0 (identical).</param>
/// <param name="Embedding">The generated embedding vector, or null if generation failed.</param>
public record PlagiarismResult(float Score, float[]? Embedding);

/// <summary>
/// Detects code plagiarism by embedding submissions and comparing cosine
/// similarity against prior submissions for the same question.
/// </summary>
public interface IPlagiarismDetector
{
    /// <summary>
    /// Analyzes the given code for plagiarism against previous submissions
    /// by the same question. Returns the similarity score and the embedding
    /// (which should be stored on the submission for future comparisons).
    /// </summary>
    Task<PlagiarismResult> DetectAsync(
        string code,
        string language,
        string userId,
        Guid questionId,
        CancellationToken cancellationToken = default);
}
