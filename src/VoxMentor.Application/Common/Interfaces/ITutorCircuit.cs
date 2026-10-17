namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Read-only view of the Ollama circuit breaker for the AI tutor (#72).
/// Lets the ask path fail fast with 503 instead of accepting sessions that
/// generation cannot start while Ollama is down.
/// </summary>
public interface ITutorCircuit
{
    /// <summary>True while the breaker is open (Ollama failing, cooling down).</summary>
    bool IsOpen { get; }
}
