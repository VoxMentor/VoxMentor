using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Services;

/// <summary>
/// Entity Framework-backed implementation of <see cref="IQuestionBank"/>.
/// Reads attempt data from <see cref="CodeSubmission"/> and hint usage from
/// <see cref="QuestionHintUsage"/> to populate the five rubric factor inputs.
/// Bypasses the <see cref="IUserOwned"/> global query filter so all users'
/// attempts are aggregated for a question (cross-user calibration).
/// </summary>
public class QuestionBank : IQuestionBank
{
    private const float PlagiarismFlagThreshold = 0.7f; // issue #56: >0.7 likely copied

    private readonly IApplicationDbContext _db;

    public QuestionBank(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<QuestionInputs> GetInputsAsync(
        Guid questionId,
        CancellationToken cancellationToken = default)
    {
        var submissions = await _db.CodeSubmissions
            .IgnoreQueryFilters()
            .Where(s => s.QuestionId == questionId)
            .ToListAsync(cancellationToken);

        var attemptCount = submissions.Count;
        if (attemptCount == 0)
        {
            return new QuestionInputs(0, 0f, null, 0f, 0.5f, 0f);
        }

        var acceptedCount = submissions.Count(s => s.IsCorrect);
        var acceptanceRate = acceptedCount / (float)attemptCount;

        var times = submissions
            .Where(s => s.ExecutionTimeMs.HasValue)
            .Select(s => s.ExecutionTimeMs!.Value)
            .OrderBy(t => t)
            .ToList();
        var medianTimeMs = times.Count > 0 ? times[times.Count / 2] : (int?)null;

        // Hint usage rate across attempts: a hint on one of ten attempts is 0.1,
        // not 1.0. Count distinct attempts (CodeSubmissions) as the denominator
        // so a single true record can't inflate the difficulty proposal.
        var hintUsages = await _db.QuestionHintUsages
            .IgnoreQueryFilters()
            .Where(h => h.QuestionId == questionId)
            .ToListAsync(cancellationToken);
        var usedHintCount = hintUsages.Count(h => h.UsedHint);
        var hintUsageRate = attemptCount > 0 ? usedHintCount / (float)attemptCount : 0f;

        // AI evaluation quality: average Correctness.Score / 10 across submissions with AiEvaluation.
        var aiQuality = ComputeAiEvaluationQuality(submissions);

        // Plagiarism flag rate: fraction flagged above threshold.
        var flaggedCount = submissions.Count(s => s.PlagiarismScore >= PlagiarismFlagThreshold);
        var plagiarismFlagRate = flaggedCount / (float)attemptCount;

        return new QuestionInputs(
            attemptCount,
            acceptanceRate,
            medianTimeMs,
            hintUsageRate,
            aiQuality,
            plagiarismFlagRate);
    }

    private static float ComputeAiEvaluationQuality(List<CodeSubmission> submissions)
    {
        var totalQuality = 0f;
        var evalCount = 0;

        foreach (var submission in submissions)
        {
            if (string.IsNullOrEmpty(submission.AiEvaluation))
            {
                continue;
            }

            try
            {
                var eval = JsonSerializer.Deserialize<CodeEvaluation>(submission.AiEvaluation);
                if (eval?.Correctness.Score is > 0 and <= 10)
                {
                    totalQuality += eval.Correctness.Score / 10f;
                    evalCount++;
                }
            }
            catch
            {
                // Skip malformed AiEvaluation JSON.
            }
        }

        return evalCount > 0 ? totalQuality / evalCount : 0.5f;
    }
}
