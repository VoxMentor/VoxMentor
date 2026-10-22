using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Failure-count circuit breaker for the AI tutor's Ollama generation (#72):
/// opens after 5 failed generations inside a 30s window — successes do not
/// mask the count — and stays open for 30s, after which probes flow again
/// until one succeeds (close) or fails (reopen). Outcomes are reported by
/// <see cref="TutorService"/> at the full-stream boundary, so HTTP, body-read,
/// and NDJSON failures all count. Shared singleton: ITutorCircuit feeds the
/// ask path's fast 503 while <see cref="IsOpen"/> is true.
/// </summary>
/// <remarks>
/// Replaces the Polly HTTP-level breaker from the first #72 pass: Polly's
/// FailureRatio/MinimumThroughput counts total calls (3 of 5 failures could
/// open), and its handler only saw header arrival, not the streamed body.
/// </remarks>
public sealed class TutorCircuitState : ITutorCircuit
{
    private const int FailureThreshold = 5;
    private static readonly TimeSpan SamplingWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly Queue<DateTime> _failures = new();
    private DateTime? _openUntil;

    /// <summary>Clock for window/break math; tests inject a fake TimeProvider.</summary>
    public TutorCircuitState(TimeProvider? clock = null)
        => _clock = clock ?? TimeProvider.System;

    /// <summary>True while open; false while closed or half-open (probe admitted).</summary>
    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return IsOpenAt(_clock.GetUtcNow().UtcDateTime);
            }
        }
    }

    /// <summary>
    /// Records a fully successful generation. A probe success closes the
    /// breaker; in the closed state successes never clear the failure window
    /// (the threshold is 5 failures regardless of successes).
    /// </summary>
    public void RecordSuccess()
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            if (_openUntil is not { } openUntil)
            {
                Prune(now);
                return;
            }

            if (now < openUntil)
            {
                return; // still open — short-circuited calls never reach Ollama
            }

            _openUntil = null; // half-open probe succeeded → closed
            _failures.Clear();
        }
    }

    /// <summary>
    /// Records one failed generation. In the closed state the 5th failure
    /// inside the sampling window opens the breaker; a failed half-open probe
    /// reopens it immediately.
    /// </summary>
    public void RecordFailure()
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            if (_openUntil is { } openUntil)
            {
                if (now < openUntil)
                {
                    return; // already open
                }

                _openUntil = now + BreakDuration; // half-open probe failed → reopen
                _failures.Clear();
                return;
            }

            Prune(now);
            _failures.Enqueue(now);
            if (_failures.Count >= FailureThreshold)
            {
                _openUntil = now + BreakDuration;
            }
        }
    }

    /// <summary>True only inside the break duration (closed and half-open read false).</summary>
    private bool IsOpenAt(DateTime now) => _openUntil is { } until && now < until;

    /// <summary>Drops failure timestamps that fell out of the sampling window.</summary>
    private void Prune(DateTime now)
    {
        while (_failures.Count > 0 && now - _failures.Peek() >= SamplingWindow)
        {
            _failures.Dequeue();
        }
    }
}
