using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="JdParseService"/>: schema request shape, JSON
/// parse, and the null-on-failure contract that drives the heuristic
/// fallback (#117).
/// </summary>
public class JdParseServiceTests
{
    /// <summary>Stub that records the request and returns a fixed response body.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public StubHandler(HttpStatusCode statusCode = HttpStatusCode.OK, string body = "{}")
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }

    private static JdParseService CreateService(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:BaseUrl"] = "http://ollama.test",
                ["Ollama:Model"] = "test-model"
            })
            .Build();

        return new JdParseService(
            new HttpClient(handler), config, NullLogger<JdParseService>.Instance);
    }

    private static string ChatResponse(string content) =>
        $"{{\"message\":{{\"content\":{System.Text.Json.JsonSerializer.Serialize(content)}}}}}";

    private const string ValidParseJson =
        """
        {"companyName":"Amazon","role":"SDE-1",
         "technicalSkills":{"Arrays":0.5,"DP":0.3,"Graphs":0.2},
         "hrSkills":{"Leadership":1},
         "difficulty":"Medium-Hard","estimatedWeeks":6}
        """;

    [Fact]
    public async Task ParseAsync_ValidResponse_ReturnsResult()
    {
        var handler = new StubHandler(body: ChatResponse(ValidParseJson));
        var service = CreateService(handler);

        var result = await service.ParseAsync("some job description text long enough to parse");

        Assert.NotNull(result);
        Assert.Equal("Amazon", result.CompanyName);
        Assert.Equal("SDE-1", result.Role);
        Assert.Equal(3, result.TechnicalSkills.Count);
        Assert.Equal(0.5f, result.TechnicalSkills["Arrays"]);
        Assert.Equal("Medium-Hard", result.Difficulty);
        Assert.Equal(6, result.EstimatedWeeks);
    }

    [Fact]
    public async Task ParseAsync_SendsSchemaAndZeroTemperature()
    {
        var handler = new StubHandler(body: ChatResponse(ValidParseJson));
        var service = CreateService(handler);

        await service.ParseAsync("some job description text long enough to parse");

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"format\"", handler.LastRequestBody);
        Assert.Contains("\"technicalSkills\"", handler.LastRequestBody);
        Assert.Contains("\"temperature\":0", handler.LastRequestBody);
        Assert.Contains("\"stream\":false", handler.LastRequestBody);
    }

    [Fact]
    public async Task ParseAsync_HttpFailure_ReturnsNull()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError);
        var service = CreateService(handler);

        var result = await service.ParseAsync("text");

        Assert.Null(result); // degrade → caller uses heuristics
    }

    [Fact]
    public async Task ParseAsync_MalformedJson_ReturnsNull()
    {
        var handler = new StubHandler(body: ChatResponse("not json at all"));
        var service = CreateService(handler);

        var result = await service.ParseAsync("text");

        Assert.Null(result);
    }

    [Fact]
    public async Task ParseAsync_UnreachableOllama_ReturnsNull()
    {
        var service = CreateService(new UnreachableHandler());

        var result = await service.ParseAsync("text");

        Assert.Null(result);
    }

    [Fact]
    public async Task ParseAsync_InternalTimeout_ReturnsNull()
    {
        // Simulates the linked-CTS CancelAfter(60s) firing: HttpClient throws
        // TaskCanceledException while the CALLER token is still live. This must
        // degrade to null (→ heuristics, 200), not escape as a 500 (#117 review).
        var service = CreateService(new TimingOutHandler());

        var result = await service.ParseAsync("text");

        Assert.Null(result);
    }

    [Fact]
    public async Task ParseAsync_CallerCancelled_Propagates()
    {
        // The caller's own cancellation is not ours to swallow — 499/abort path.
        var service = CreateService(new StubHandler());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ParseAsync("text", cts.Token));
    }

    private sealed class TimingOutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("simulated 60s cap");
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    [Fact]
    public void ParseResult_MissingFields_GetSafeDefaults()
    {
        var result = JdParseService.ParseResult("{}");

        Assert.Equal("Unknown", result.CompanyName);
        Assert.Equal("Unknown", result.Role);
        Assert.Equal("Medium", result.Difficulty);
        Assert.Equal(6, result.EstimatedWeeks);
        Assert.Empty(result.TechnicalSkills);
        Assert.Empty(result.HrSkills);
    }

    [Fact]
    public void ParseResult_BlankStrings_GetSafeDefaults()
    {
        // Blank company/role/difficulty are as invalid as null (CodeRabbit #131).
        var result = JdParseService.ParseResult(
            """{"companyName":"  ","role":"","difficulty":"   "}""");

        Assert.Equal("Unknown", result.CompanyName);
        Assert.Equal("Unknown", result.Role);
        Assert.Equal("Medium", result.Difficulty);
    }

    [Fact]
    public void ParseResult_NullSkillMaps_GetEmptyMaps()
    {
        var result = JdParseService.ParseResult(
            """{"companyName":"X","role":"Y","technicalSkills":null,"hrSkills":null,"difficulty":"Hard","estimatedWeeks":3}""");

        Assert.Empty(result.TechnicalSkills);
        Assert.Empty(result.HrSkills);
        Assert.Equal(3, result.EstimatedWeeks);
    }

    [Fact]
    public void ParseResult_NonPositiveWeeks_GetDefault()
    {
        var result = JdParseService.ParseResult(
            """{"companyName":"X","role":"Y","technicalSkills":{},"hrSkills":{},"difficulty":"Hard","estimatedWeeks":0}""");

        Assert.Equal(6, result.EstimatedWeeks);
    }
}
