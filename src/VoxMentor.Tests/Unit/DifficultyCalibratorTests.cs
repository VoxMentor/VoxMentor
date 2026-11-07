using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="DifficultyCalibrator"/> covering agreement,
/// contradiction, and insufficient-data scenarios.
/// </summary>
public class DifficultyCalibratorTests
{
    private readonly DifficultyCalibrator _calibrator = new();

    [Fact]
    public void Calibrate_AllFactorsAgree_Hard_ProposesHighDifficulty()
    {
        var inputs = new QuestionInputs(
            AttemptCount: 100,
            AcceptanceRate: 0.2f,
            MedianTimeToSubmitMs: 600_000,
            HintUsageRate: 0.8f,
            AiEvaluationQuality: 0.2f,
            PlagiarismFlagRate: 0.1f);

        var proposal = _calibrator.Calibrate(inputs);

        Assert.True(proposal.HasSufficientData);
        Assert.NotNull(proposal.ProposedDifficulty);
        Assert.InRange(proposal.ProposedDifficulty.Value, 1, 10);
        Assert.Equal(9, proposal.ProposedDifficulty);
        Assert.Equal(1f, proposal.Confidence);
    }

    [Fact]
    public void Calibrate_OneFactorContradicts_MajorityWins()
    {
        // Four factors say easy, one (plagiarism) says hard → low-mid difficulty.
        var inputs = new QuestionInputs(
            AttemptCount: 50,
            AcceptanceRate: 0.9f,
            MedianTimeToSubmitMs: 0,
            HintUsageRate: 0.0f,
            AiEvaluationQuality: 0.9f,
            PlagiarismFlagRate: 0.0f);

        var proposal = _calibrator.Calibrate(inputs);

        Assert.True(proposal.HasSufficientData);
        Assert.NotNull(proposal.ProposedDifficulty);
        Assert.InRange(proposal.ProposedDifficulty.Value, 1, 10);
        Assert.Equal(3, proposal.ProposedDifficulty);
        Assert.Equal(1f, proposal.Confidence);
    }

    [Fact]
    public void Calibrate_InsufficientData_NoProposal()
    {
        var inputs = new QuestionInputs(
            AttemptCount: DifficultyCalibrator.MinAttempts - 1,
            AcceptanceRate: 0.5f,
            MedianTimeToSubmitMs: 150_000,
            HintUsageRate: 0.5f,
            AiEvaluationQuality: 0.5f,
            PlagiarismFlagRate: 0.5f);

        var proposal = _calibrator.Calibrate(inputs);

        Assert.False(proposal.HasSufficientData);
        Assert.Null(proposal.ProposedDifficulty);
        Assert.Equal(0f, proposal.Confidence);
    }

    [Fact]
    public void Calibrate_ExactlyMinAttempts_SufficientData()
    {
        var inputs = new QuestionInputs(
            AttemptCount: DifficultyCalibrator.MinAttempts,
            AcceptanceRate: 0.5f,
            MedianTimeToSubmitMs: 150_000,
            HintUsageRate: 0.5f,
            AiEvaluationQuality: 0.5f,
            PlagiarismFlagRate: 0.5f);

        var proposal = _calibrator.Calibrate(inputs);

        // All factors neutral → score 0.5 → difficulty round(0.5*9+1) = round(5.5) = 6
        Assert.True(proposal.HasSufficientData);
        Assert.Equal(6, proposal.ProposedDifficulty);
        Assert.Equal(DifficultyCalibrator.MinAttempts / (float)DifficultyCalibrator.FullConfidenceAttempts, proposal.Confidence);
    }
}
