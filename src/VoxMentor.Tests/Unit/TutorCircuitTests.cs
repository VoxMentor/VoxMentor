using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Circuit breaker acceptance for #72: opens after exactly five failed
/// generations (successes don't mask the count), stays open for the break
/// duration, then admits a probe that either closes or reopens the breaker.
/// </summary>
public class TutorCircuitTests
{
    /// <summary>Clock stub that can be advanced manually.</summary>
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

        /// <summary>Advances the stub clock by the given span.</summary>
        public void Advance(TimeSpan by) => _now += by;

        /// <summary>Returns the current stubbed time.</summary>
        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>Creates a TutorCircuitState paired with its controllable clock.</summary>
    private static (TutorCircuitState Circuit, FakeClock Clock) CreateCircuit()
    {
        var clock = new FakeClock();
        return (new TutorCircuitState(clock), clock);
    }

    /// <summary>The fifth recorded failure opens the circuit; four do not.</summary>
    [Fact]
    public void RecordFailure_FiveFailures_OpensCircuit()
    {
        var (circuit, _) = CreateCircuit();

        for (var i = 0; i < 4; i++)
        {
            circuit.RecordFailure();
        }

        Assert.False(circuit.IsOpen);

        circuit.RecordFailure();
        Assert.True(circuit.IsOpen);
    }

    /// <summary>Successes do not reset the failure count; five failures still open the circuit.</summary>
    [Fact]
    public void RecordFailure_InterleavedSuccesses_StillOpensOnFifthFailure()
    {
        // The threshold counts failed calls regardless of successful calls:
        // 3 failures + 2 successes must NOT open, 5 failures + successes must.
        var (circuit, _) = CreateCircuit();

        circuit.RecordFailure();
        circuit.RecordSuccess();
        circuit.RecordFailure();
        circuit.RecordSuccess();
        circuit.RecordFailure();
        Assert.False(circuit.IsOpen);

        circuit.RecordFailure();
        circuit.RecordSuccess();
        circuit.RecordFailure();

        Assert.True(circuit.IsOpen);
    }

    /// <summary>Failures spaced outside the sampling window do not accumulate toward opening.</summary>
    [Fact]
    public void RecordFailure_FailuresOutsideSamplingWindow_DoNotAccumulate()
    {
        var (circuit, clock) = CreateCircuit();

        for (var i = 0; i < 5; i++)
        {
            circuit.RecordFailure();
            clock.Advance(TimeSpan.FromSeconds(31));
        }

        Assert.False(circuit.IsOpen);
    }

    /// <summary>After the break duration the circuit allows a probe; a failed probe reopens it immediately.</summary>
    [Fact]
    public void IsOpen_AfterBreakDuration_ExpiresAndProbeFailureReopens()
    {
        var (circuit, clock) = CreateCircuit();

        for (var i = 0; i < 5; i++)
        {
            circuit.RecordFailure();
        }

        Assert.True(circuit.IsOpen);

        // Break duration elapses: probes flow again (IsOpen false).
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.False(circuit.IsOpen);

        // A failed probe reopens immediately.
        circuit.RecordFailure();
        Assert.True(circuit.IsOpen);
    }

    /// <summary>A successful probe after the break closes the circuit and resets the failure count.</summary>
    [Fact]
    public void IsOpen_ProbeSuccessAfterBreakDuration_Closes()
    {
        var (circuit, clock) = CreateCircuit();

        for (var i = 0; i < 5; i++)
        {
            circuit.RecordFailure();
        }

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.False(circuit.IsOpen);

        circuit.RecordSuccess();
        Assert.False(circuit.IsOpen);

        // Closed again: failures must re-accumulate from zero.
        circuit.RecordFailure();
        Assert.False(circuit.IsOpen);
    }

    /// <summary>A stale success recorded while open does not close the circuit.</summary>
    [Fact]
    public void RecordSuccess_WhileOpen_DoesNotClose()
    {
        var (circuit, _) = CreateCircuit();

        for (var i = 0; i < 5; i++)
        {
            circuit.RecordFailure();
        }

        // No call can reach Ollama while open, so a success report is stale:
        // it must not silently close a breaker that hasn't cooled down.
        circuit.RecordSuccess();
        Assert.True(circuit.IsOpen);
    }
}
