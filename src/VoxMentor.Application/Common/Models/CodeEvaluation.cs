namespace VoxMentor.Application.Common.Models;

/// <summary>
/// AI evaluation of a submitted solution across four dimensions:
/// correctness, time complexity, space complexity, and code style.
/// Serialized into <c>CodeSubmissions.AiEvaluation</c> as JSON.
/// </summary>
public class CodeEvaluation
{
    /// <summary>Correctness assessment.</summary>
    public DimensionScore Correctness { get; set; } = new(1, 10, string.Empty);

    /// <summary>Time complexity analysis.</summary>
    public ComplexityScore TimeComplexity { get; set; } = new(string.Empty, false);

    /// <summary>Space complexity analysis.</summary>
    public ComplexityScore SpaceComplexity { get; set; } = new(string.Empty, false);

    /// <summary>Code style assessment.</summary>
    public DimensionScore CodeStyle { get; set; } = new(1, 10, string.Empty);
}

/// <summary>A scored dimension on a fixed 1-10 scale.</summary>
/// <param name="Score">Score from 1 (worst) to 10 (best).</param>
/// <param name="MaxScore">Maximum attainable score (10).</param>
/// <param name="Feedback">Short explanatory feedback for the student.</param>
public record DimensionScore(int Score, int MaxScore, string Feedback);

/// <summary>A Big-O complexity dimension.</summary>
/// <param name="BigO">Complexity class in Big-O notation, e.g. "O(n log n)".</param>
/// <param name="IsOptimal">Whether the complexity is optimal for the problem type.</param>
/// <param name="Feedback">Optional explanatory feedback.</param>
public record ComplexityScore(string BigO, bool IsOptimal, string? Feedback = null);
