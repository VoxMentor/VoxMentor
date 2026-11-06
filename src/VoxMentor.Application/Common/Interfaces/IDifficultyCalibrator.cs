namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Scores a question on five rubric factors and proposes a corrected
/// difficulty plus confidence. Pure logic: no database access.
/// </summary>
public interface IDifficultyCalibrator
{
    /// <summary>
    /// Calibrates the difficulty for a question based on the five rubric
    /// factor inputs. Returns <see cref="DifficultyProposal.HasSufficientData"/>
    /// false when there are too few attempts to propose a change.
    /// </summary>
    DifficultyProposal Calibrate(QuestionInputs inputs);
}

/// <summary>
/// The output of difficulty calibration.
/// </summary>
/// <param name="ProposedDifficulty">Proposed difficulty (1-10), or null if insufficient data.</param>
/// <param name="Confidence">Confidence in the proposal (0-1).</param>
/// <param name="HasSufficientData">True when attempt count meets the minimum threshold.</param>
public record DifficultyProposal(int? ProposedDifficulty, float Confidence, bool HasSufficientData);
