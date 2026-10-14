using System.Net;
using Polly;
using Polly.CircuitBreaker;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Circuit breaker acceptance for #72: opens after 5 Ollama failures, stays
/// open for the break duration, and short-circuits calls while open.
/// </summary>
public class TutorCircuitTests
{
    private static ResiliencePipeline<HttpResponseMessage> CreatePipeline(
        CircuitBreakerStateProvider state)
        => new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(TutorCircuit.Create(state))
            .Build();

    private static ValueTask<HttpResponseMessage> FailingCall(CancellationToken cancellationToken)
        => new(new HttpResponseMessage(HttpStatusCode.InternalServerError));

    [Fact]
    public async Task Execute_FiveFailures_OpensCircuitAndShortCircuits()
    {
        var state = new CircuitBreakerStateProvider();
        var pipeline = CreatePipeline(state);
        var view = new TutorCircuitState(state);

        for (var i = 0; i < 5; i++)
        {
            await pipeline.ExecuteAsync(FailingCall, CancellationToken.None);
        }

        Assert.True(view.IsOpen);
        // Open circuit: the delegate must not run again.
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => pipeline.ExecuteAsync(FailingCall, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Execute_FewerThanFiveFailures_StaysClosed()
    {
        var state = new CircuitBreakerStateProvider();
        var pipeline = CreatePipeline(state);
        var view = new TutorCircuitState(state);

        for (var i = 0; i < 4; i++)
        {
            await pipeline.ExecuteAsync(FailingCall, CancellationToken.None);
        }

        Assert.False(view.IsOpen);
    }

    [Fact]
    public async Task Execute_MajoritySuccesses_StaysClosed()
    {
        var state = new CircuitBreakerStateProvider();
        var pipeline = CreatePipeline(state);
        var view = new TutorCircuitState(state);

        // 5 calls, 2 failures → ratio 0.4 < 0.5: a few failures among mostly
        // successes must not trip the breaker.
        await pipeline.ExecuteAsync(FailingCall, CancellationToken.None);
        await pipeline.ExecuteAsync(_ => new ValueTask<HttpResponseMessage>(
            new HttpResponseMessage(HttpStatusCode.OK)), CancellationToken.None);
        await pipeline.ExecuteAsync(FailingCall, CancellationToken.None);
        await pipeline.ExecuteAsync(_ => new ValueTask<HttpResponseMessage>(
            new HttpResponseMessage(HttpStatusCode.OK)), CancellationToken.None);
        await pipeline.ExecuteAsync(_ => new ValueTask<HttpResponseMessage>(
            new HttpResponseMessage(HttpStatusCode.OK)), CancellationToken.None);

        Assert.False(view.IsOpen);
    }
}
