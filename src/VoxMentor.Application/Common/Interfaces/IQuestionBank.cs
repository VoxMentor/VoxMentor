namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Mockable input abstraction for the difficulty calibration scorer.
/// Provides the five rubric factors per question without exposing EF Core
/// to the pure scoring service.
/// </summary>
public interface IQuestionBank
{
    /// <summary>
    /// Returns the five scoring inputs for a question: attempt count,
    /// acceptance rate, median time-to-submit, hint usage rate, AI
    /// evaluation quality, and plagiarism flag rate.
    /// </summary>
    Task<QuestionInputs> GetInputsAsync(Guid questionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The five rubric factor inputs for difficulty calibration.
/// All rates are normalized 0-1; <see cref="MedianTimeToSubmitMs"/> is raw ms.
/// </summary>
/// <param name="AttemptCount">Total number of attempts for the question.</param>
/// <param name="AcceptanceRate">Fraction of attempts marked correct (0-1).</param>
/// <param name="MedianTimeToSubmitMs">Median execution time across attempts, or null.</param>
/// <param name="HintUsageRate">Fraction of attempts where a hint was used (0-1).</param>
/// <param name="AiEvaluationQuality">Average AI correctness score normalized 0-1.</param>
/// <param name="PlagiarismFlagRate">Fraction of attempts flagged as plagiarism (0-1).</param>
public record QuestionInputs(
    int AttemptCount,
    float AcceptanceRate,
    int? MedianTimeToSubmitMs,
    float HintUsageRate,
    float AiEvaluationQuality,
    float PlagiarismFlagRate);
