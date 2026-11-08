using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Application.Services;

/// <summary>
/// Pure difficulty calibration scorer. Scores a question on five rubric
/// factors and proposes a corrected difficulty (1-10) plus confidence.
/// No database access — inputs come through <see cref="QuestionInputs"/>.
/// </summary>
public class DifficultyCalibrator : IDifficultyCalibrator
{
    /// <summary>Minimum attempts required before proposing a difficulty change.</summary>
    public const int MinAttempts = 10;

    /// <summary>Attempt count at which confidence reaches 1.0.</summary>
    public const int FullConfidenceAttempts = 50;

    /// <summary>Reference median time (ms) above which a question is considered hard.</summary>
    public const int ReferenceTimeMs = 300_000; // 5 minutes

    /// <summary>
    /// Per-factor weights. Sums to 1.0; tuning is a one-line change here.
    /// </summary>
    private static readonly (string Factor, float Weight)[] Weights =
    {
        ("AcceptanceRate", 0.25f),
        ("TimeToSubmit", 0.20f),
        ("HintUsage", 0.15f),
        ("AiEvaluation", 0.20f),
        ("Plagiarism", 0.20f)
    };

    /// <inheritdoc />
    public DifficultyProposal Calibrate(QuestionInputs inputs)
    {
        if (inputs.AttemptCount < MinAttempts)
        {
            return new DifficultyProposal(null, 0f, false);
        }

        // Normalize each factor to a difficulty signal (0 = easiest, 1 = hardest).
        var acceptanceSignal = 1f - inputs.AcceptanceRate;              // low acceptance = hard
        var timeSignal = Math.Clamp(
            inputs.MedianTimeToSubmitMs.GetValueOrDefault() / (float)ReferenceTimeMs, 0f, 1f);
        var hintSignal = inputs.HintUsageRate;                          // more hints = hard
        var aiSignal = 1f - inputs.AiEvaluationQuality;                 // low AI quality = hard
        var plagiarismSignal = 1f - inputs.PlagiarismFlagRate;          // high plagiarism = easy (trivially searchable)

        var score = Weights[0].Weight * acceptanceSignal +
                    Weights[1].Weight * timeSignal +
                    Weights[2].Weight * hintSignal +
                    Weights[3].Weight * aiSignal +
                    Weights[4].Weight * plagiarismSignal;

        // Map 0-1 score to 1-10 difficulty.
        var proposedDifficulty = Math.Clamp((int)Math.Round(score * 9 + 1), 1, 10);

        // Confidence grows with attempt count, full at FullConfidenceAttempts.
        var confidence = Math.Clamp(inputs.AttemptCount / (float)FullConfidenceAttempts, 0f, 1f);

        return new DifficultyProposal(proposedDifficulty, confidence, true);
    }
}
