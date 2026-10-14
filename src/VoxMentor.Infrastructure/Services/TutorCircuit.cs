using Polly;
using Polly.CircuitBreaker;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Circuit breaker for the tutor's Ollama HttpClient (#72): 5 failures among
/// ≥5 calls within 30s open the circuit for 30s; while open, calls short-
/// circuit with <see cref="BrokenCircuitException"/>. Shared state provider
/// lets the ask path fail fast with 503 via <see cref="ITutorCircuit"/>.
/// </summary>
public static class TutorCircuit
{
    /// <summary>
    /// Single source for the breaker configuration — used by DI and exercised
    /// directly by tests so the "5 failures → open 30s" acceptance criterion
    /// is verified against the production options.
    /// </summary>
    public static CircuitBreakerStrategyOptions<HttpResponseMessage> Create(
        CircuitBreakerStateProvider stateProvider)
        => new()
        {
            StateProvider = stateProvider,
            // 5 failures (MinimumThroughput at ratio 0.5 = ≥5 failures among
            // ≥5 calls) inside the 30s window open the circuit — the issue's
            // "5 failures" rule expressed in ratio terms so a mixed
            // success/failure outage still trips it.
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(30),
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .HandleResult(r => !r.IsSuccessStatusCode)
                .Handle<HttpRequestException>()
                // HttpClient.Timeout surfaces as TaskCanceledException with a
                // TimeoutException inner — count it; plain cancellation (client
                // disconnect) must not trip the breaker.
                .Handle<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
        };
}

/// <summary>Read-only view of <see cref="TutorCircuit"/>'s shared state.</summary>
public sealed class TutorCircuitState : ITutorCircuit
{
    private readonly CircuitBreakerStateProvider _stateProvider;

    public TutorCircuitState(CircuitBreakerStateProvider stateProvider)
    {
        _stateProvider = stateProvider;
    }

    public bool IsOpen => _stateProvider.CircuitState == CircuitState.Open;
}
