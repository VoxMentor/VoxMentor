using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Evaluates submitted code with an AI model, scoring correctness,
/// time/space complexity, and code style.
/// </summary>
public interface ICodeEvaluator
{
    /// <summary>
    /// Evaluates the given source code and returns the four-dimension
    /// evaluation (correctness, time complexity, space complexity, code style).
    /// </summary>
    Task<CodeEvaluation> EvaluateAsync(string code, string language, CancellationToken cancellationToken = default);
}
