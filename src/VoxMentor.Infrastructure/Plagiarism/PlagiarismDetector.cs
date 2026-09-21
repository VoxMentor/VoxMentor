using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Plagiarism;

/// <summary>
/// Detects plagiarism by embedding code via Ollama and querying pgvector
/// cosine similarity against prior submissions for the same question.
///
/// Thresholds (from issue #56):
///   > 0.9  → identical (same code, possibly copy-paste)
///   > 0.7  → likely copied (renamed variables, minor changes)
///   <  0.3 → original (different approach)
/// </summary>
public class PlagiarismDetector : IPlagiarismDetector
{
    private readonly CodeEmbeddingService _embeddingService;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<PlagiarismDetector> _logger;

    public PlagiarismDetector(
        CodeEmbeddingService embeddingService,
        IApplicationDbContext db,
        ILogger<PlagiarismDetector> logger)
    {
        _embeddingService = embeddingService;
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PlagiarismResult> DetectAsync(
        string code,
        string language,
        string userId,
        Guid questionId,
        CancellationToken cancellationToken = default)
    {
        // Generate embedding for the new code
        var embedding = await _embeddingService.EmbedAsync(code, cancellationToken);
        if (embedding is null)
        {
            _logger.LogDebug("Embedding unavailable, skipping plagiarism check");
            return new PlagiarismResult(0f, null);
        }

        // Query pgvector for most similar prior submissions (same question, different user)
        var embeddingJson = JsonSerializer.Serialize(embedding);

        // ponytail: raw SQL for pgvector cosine distance — EF Core doesn't natively translate <=>" operator.
        // ponytail: embeddingJson is parameterized via NpgsqlParameter, safe from injection.
        var similarSubmissions = await _db.CodeSubmissions
            .FromSqlRaw("""
                SELECT "Id", "UserId", "CodeEmbedding"::text AS "CodeEmbedding"
                FROM "CodeSubmissions"
                WHERE "QuestionId" = {0}
                  AND "UserId" != {1}
                  AND "CodeEmbedding" IS NOT NULL
                ORDER BY "CodeEmbedding"::vector <=> {2}::vector
                LIMIT 5
                """, questionId, userId, embeddingJson)
            .Select(s => new { s.Id, s.CodeEmbedding })
            .ToListAsync(cancellationToken);

        if (similarSubmissions.Count == 0)
        {
            _logger.LogDebug("No prior submissions to compare against for question {QuestionId}", questionId);
            return new PlagiarismResult(0f, embedding);
        }

        // Calculate cosine similarity for each candidate
        var bestScore = 0f;
        foreach (var candidate in similarSubmissions)
        {
            if (string.IsNullOrEmpty(candidate.CodeEmbedding))
                continue;

            float[]? candidateEmbedding;
            try
            {
                candidateEmbedding = JsonSerializer.Deserialize<float[]>(candidate.CodeEmbedding);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Malformed embedding JSON in submission {SubmissionId}, skipping", candidate.Id);
                continue;
            }

            if (candidateEmbedding is null || candidateEmbedding.Length != embedding.Length)
                continue;

            var similarity = CosineSimilarity(embedding, candidateEmbedding);
            if (similarity > bestScore)
                bestScore = similarity;
        }

        _logger.LogDebug(
            "Plagiarism check for question {QuestionId}: score={Score:F3} ({Count} candidates compared)",
            questionId, bestScore, similarSubmissions.Count);

        return new PlagiarismResult(Math.Clamp(bestScore, 0f, 1f), embedding);
    }

    /// <summary>
    /// Computes cosine similarity between two vectors.
    /// Returns value between -1 (opposite) and 1 (identical).
    /// </summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vector dimensions mismatch: {a.Length} vs {b.Length}");

        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0 ? 0 : dot / denominator;
    }
}
