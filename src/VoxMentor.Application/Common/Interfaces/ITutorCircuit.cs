namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Circuit breaker for the AI tutor's Ollama generation (#72). Lets the ask
/// path fail fast with 503 instead of accepting sessions that generation
/// cannot start while Ollama is down; generation reports outcomes so the
/// breaker counts full-stream failures, not just HTTP header failures.
/// </summary>
public interface ITutorCircuit
{
    /// <summary>True while the breaker is open (Ollama failing, cooling down).</summary>
    bool IsOpen { get; }

    /// <summary>Records one fully successful generation (final stream chunk seen).</summary>
    void RecordSuccess();

    /// <summary>Records one failed generation (HTTP, body read, or NDJSON failure).</summary>
    void RecordFailure();
}
