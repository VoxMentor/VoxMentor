using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VoxMentor.Application.Features.MockInterviews;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Verdict contract for #148: <see cref="OllamaMockInterviewGenerator.AssessAnswerSolvedAsync"/>
/// parses the structured Ollama reply and degrades to null on any failure (the
/// caller probes instead of stalling) — while caller cancellation still propagates.
/// </summary>
public class OllamaMockInterviewGeneratorTests
{
    private sealed class StubHttp : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        public StubHttp(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond());
    }

    private static OllamaMockInterviewGenerator Create(Func<HttpResponseMessage> respond)
        => new(
            new HttpClient(new StubHttp(respond)),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Ollama:BaseUrl"] = "http://ollama.test",
                    ["Ollama:Model"] = "test-model"
                })
                .Build(),
            NullLogger<OllamaMockInterviewGenerator>.Instance);

    /// <summary>An /api/chat reply whose assistant content is the given string.</summary>
    private static HttpResponseMessage ChatReply(
        string content, HttpStatusCode status = HttpStatusCode.OK)
        => new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { message = new { content } }),
                Encoding.UTF8,
                "application/json")
        };

    private static readonly IReadOnlyList<TranscriptTurn> Turns =
        new[] { new TranscriptTurn(TranscriptTurn.CandidateRole, "my answer", null, DateTime.UtcNow) };

    [Fact]
    public async Task AssessAnswerSolved_SolvedTrue_ReturnsTrue()
    {
        var generator = Create(() => ChatReply("""{"solved":true}"""));
        Assert.True(await generator.AssessAnswerSolvedAsync(Turns, InterviewType.Technical));
    }

    [Fact]
    public async Task AssessAnswerSolved_MissingFlag_ReadsFalse_ExtraProbeNotStall()
    {
        var generator = Create(() => ChatReply("{}"));
        Assert.False(await generator.AssessAnswerSolvedAsync(Turns, InterviewType.Technical));
    }

    [Fact]
    public async Task AssessAnswerSolved_MalformedVerdict_ReturnsNull()
    {
        var generator = Create(() => ChatReply("this is not json"));
        Assert.Null(await generator.AssessAnswerSolvedAsync(Turns, InterviewType.Behavioral));
    }

    [Fact]
    public async Task AssessAnswerSolved_HttpError_ReturnsNull()
    {
        var generator = Create(() => ChatReply("""{"solved":true}""", HttpStatusCode.InternalServerError));
        Assert.Null(await generator.AssessAnswerSolvedAsync(Turns, InterviewType.Technical));
    }

    [Fact]
    public async Task AssessAnswerSolved_OllamaTimeout_ReturnsNull()
    {
        var generator = Create(() => throw new OperationCanceledException());
        Assert.Null(await generator.AssessAnswerSolvedAsync(Turns, InterviewType.Technical));
    }

    [Fact]
    public async Task AssessAnswerSolved_CallerCancelled_ThrowsNotNull()
    {
        var generator = Create(() => ChatReply("""{"solved":true}"""));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            generator.AssessAnswerSolvedAsync(Turns, InterviewType.Technical, cts.Token));
    }
}
