using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Parses raw job-description text into structured skill weights via Ollama.
/// Returns null when Ollama is unreachable or its output is unparseable —
/// callers degrade to <c>JdHeuristics</c> instead of failing the request (#117).
/// </summary>
public interface IJdParseService
{
    Task<JdParseResult?> ParseAsync(string rawText, CancellationToken cancellationToken = default);
}
