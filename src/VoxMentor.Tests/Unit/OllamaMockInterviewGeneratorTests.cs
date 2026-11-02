using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VoxMentor.Application.Common.Interfaces;
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

        /// <summary>Bodies of every /api/chat request sent, in order — prompt assertions read these (#150).</summary>
        public List<string> SentBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                SentBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            return _respond();
        }
    }

    private static OllamaMockInterviewGenerator Create(
        Func<HttpResponseMessage> respond, Dictionary<string, string?>? extraConfig = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Ollama:BaseUrl"] = "http://ollama.test",
            ["Ollama:Model"] = "test-model"
        };
        if (extraConfig is not null)
        {
            foreach (var (key, value) in extraConfig)
            {
                settings[key] = value;
            }
        }

        return new(
            new HttpClient(new StubHttp(respond)),
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<OllamaMockInterviewGenerator>.Instance);
    }

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

    /// <summary>Generator plus its request-capturing stub, for prompt assertions (#150).</summary>
    private static (OllamaMockInterviewGenerator Generator, StubHttp Http) CreateCapturing(
        Dictionary<string, string?> extraConfig)
    {
        var http = new StubHttp(() => ChatReply("""{"solved":true}"""));
        var settings = new Dictionary<string, string?>
        {
            ["Ollama:BaseUrl"] = "http://ollama.test",
            ["Ollama:Model"] = "test-model"
        };
        foreach (var (key, value) in extraConfig)
        {
            settings[key] = value;
        }

        var generator = new OllamaMockInterviewGenerator(
            new HttpClient(http),
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<OllamaMockInterviewGenerator>.Instance);
        return (generator, http);
    }

    /// <summary>InterviewTracks section entries for the #150 tests: Amazon + Google + a custom third company (data-driven proof).</summary>
    private static Dictionary<string, string?> TrackConfig => new()
    {
        ["InterviewTracks:Amazon:Aliases:0"] = "amazon",
        ["InterviewTracks:Amazon:Aliases:1"] = "aws",
        ["InterviewTracks:Amazon:Prompt"] = "PROBE_AMAZON_LPS",
        ["InterviewTracks:Amazon:Rubric"] = "RUBRIC_AMAZON_LPS",
        ["InterviewTracks:Google:Aliases:0"] = "google",
        ["InterviewTracks:Google:Prompt"] = "PROBE_GOOGLE_GOOGLINESS",
        ["InterviewTracks:Google:Rubric"] = "RUBRIC_GOOGLE_GOOGLINESS",
        ["InterviewTracks:Contoso:Aliases:0"] = "contoso",
        ["InterviewTracks:Contoso:Prompt"] = "PROBE_CONTOSO_CUSTOM",
        ["InterviewTracks:Contoso:Rubric"] = "RUBRIC_CONTOSO_CUSTOM"
    };

    private static readonly IReadOnlyList<TranscriptTurn> Turns =
        new[] { new TranscriptTurn(TranscriptTurn.CandidateRole, "my answer", null, DateTime.UtcNow) };

    [Fact]
    public async Task AssessAnswerSolved_SolvedTrue_ReturnsTrue()
    {
        var generator = Create(() => ChatReply("""{"solved":true}"""));
        Assert.True(await generator.AssessAnswerSolvedAsync(Turns, InterviewType.Technical, null));
    }

    [Fact]
    public async Task AssessAnswerSolved_MissingFlag_ReadsFalse_ExtraProbeNotStall()
    {
        var generator = Create(() => ChatReply("{}"));
        Assert.False(await generator.AssessAnswerSolvedAsync(Turns, InterviewType.Technical, null));
    }

    [Fact]
    public async Task AssessAnswerSolved_MalformedVerdict_ReturnsNull()
    {
        var generator = Create(() => ChatReply("this is not json"));
        Assert.Null(await generator.AssessAnswerSolvedAsync(Turns, InterviewType.Behavioral, null));
    }

    [Fact]
    public async Task AssessAnswerSolved_HttpError_ReturnsNull()
    {
        var generator = Create(() => ChatReply("""{"solved":true}""", HttpStatusCode.InternalServerError));
        Assert.Null(await generator.AssessAnswerSolvedAsync(Turns, InterviewType.Technical, null));
    }

    [Fact]
    public async Task AssessAnswerSolved_OllamaTimeout_ReturnsNull()
    {
        var generator = Create(() => throw new OperationCanceledException());
        Assert.Null(await generator.AssessAnswerSolvedAsync(Turns, InterviewType.Technical, null));
    }

    [Fact]
    public async Task AssessAnswerSolved_CallerCancelled_ThrowsNotNull()
    {
        var generator = Create(() => ChatReply("""{"solved":true}"""));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            generator.AssessAnswerSolvedAsync(Turns, InterviewType.Technical, null, cts.Token));
    }

    /// <summary>Streams a method's chunks into one string for prompt assertions.</summary>
    private static async Task<string> Collect(IAsyncEnumerable<TutorChunk> chunks)
    {
        var text = new StringBuilder();
        await foreach (var chunk in chunks)
        {
            text.Append(chunk.Text);
        }
        return text.ToString();
    }

    /// <summary>Amazon JD: Prompt drives both dialogue surfaces, Rubric both judging surfaces (#150).</summary>
    [Fact]
    public async Task Track_Amazon_AllFourSurfacesContainTrackText()
    {
        var (generator, http) = CreateCapturing(TrackConfig);

        await Collect(generator.OpeningStreamAsync("Amazon", "SDE-1", "Two Sum"));
        await Collect(generator.FollowUpStreamAsync(Turns, "Amazon"));
        await generator.AssessAnswerSolvedAsync(Turns, InterviewType.Technical, "Amazon");
        await generator.ReviewAsync(Turns, InterviewType.Behavioral, "Amazon", "SDE-1");

        Assert.Equal(4, http.SentBodies.Count);
        Assert.Contains("PROBE_AMAZON_LPS", http.SentBodies[0]); // opening
        Assert.Contains("PROBE_AMAZON_LPS", http.SentBodies[1]); // follow-up
        Assert.Contains("RUBRIC_AMAZON_LPS", http.SentBodies[2]); // verdict
        Assert.Contains("RUBRIC_AMAZON_LPS", http.SentBodies[3]); // review
    }

    /// <summary>Google JD gets the Googliness track, not Amazon's — matching is per-entry, not global (#150).</summary>
    [Fact]
    public async Task Track_Google_GetsGooglinessText()
    {
        var (generator, http) = CreateCapturing(TrackConfig);

        await Collect(generator.FollowUpStreamAsync(Turns, "Google"));

        var body = Assert.Single(http.SentBodies);
        Assert.Contains("PROBE_GOOGLE_GOOGLINESS", body);
        Assert.DoesNotContain("PROBE_AMAZON_LPS", body);
    }

    /// <summary>Alias matching is case-insensitive: "AWS" resolves to the Amazon track via its configured alias (#150).</summary>
    [Fact]
    public async Task Track_AliasAws_MatchesAmazonTrack()
    {
        var (generator, http) = CreateCapturing(TrackConfig);

        await Collect(generator.FollowUpStreamAsync(Turns, "AWS"));

        var body = Assert.Single(http.SentBodies);
        Assert.Contains("PROBE_AMAZON_LPS", body);
    }

    /// <summary>Unknown company and null JD both fall back to the generic prompts — no track text leaks (#150).</summary>
    [Theory]
    [InlineData("Acme")]
    [InlineData(null)]
    [InlineData("Unknown")]
    public async Task Track_UnknownOrNullCompany_FallsBackToGenericPrompt(string? company)
    {
        var (generator, http) = CreateCapturing(TrackConfig);

        await Collect(generator.FollowUpStreamAsync(Turns, company));
        await generator.AssessAnswerSolvedAsync(Turns, InterviewType.Technical, company);

        Assert.Equal(2, http.SentBodies.Count);
        Assert.All(http.SentBodies, body =>
        {
            Assert.DoesNotContain("PROBE_", body);
            Assert.DoesNotContain("RUBRIC_", body);
        });
    }

    /// <summary>The mapping is a config table: a company that exists only in config (no code branch) resolves its track (#150).</summary>
    [Fact]
    public async Task Track_ConfigOnlyCompany_ResolvesWithoutCodeChange()
    {
        var (generator, http) = CreateCapturing(TrackConfig);

        await Collect(generator.FollowUpStreamAsync(Turns, "Contoso"));

        var body = Assert.Single(http.SentBodies);
        Assert.Contains("PROBE_CONTOSO_CUSTOM", body);
    }

    /// <summary>No InterviewTracks section at all: every surface behaves exactly like the pre-#150 generator (#150).</summary>
    [Fact]
    public async Task Track_NoConfigSection_FullyGeneric()
    {
        var (generator, http) = CreateCapturing(new Dictionary<string, string?>());

        await Collect(generator.OpeningStreamAsync("Amazon", "SDE-1", "Two Sum"));
        await Collect(generator.FollowUpStreamAsync(Turns, "Amazon"));
        await generator.AssessAnswerSolvedAsync(Turns, InterviewType.Technical, "Amazon");
        await generator.ReviewAsync(Turns, InterviewType.Technical, "Amazon", "SDE-1");

        Assert.Equal(4, http.SentBodies.Count);
        Assert.All(http.SentBodies, body =>
        {
            Assert.DoesNotContain("PROBE_", body);
            Assert.DoesNotContain("RUBRIC_", body);
        });
    }
}
