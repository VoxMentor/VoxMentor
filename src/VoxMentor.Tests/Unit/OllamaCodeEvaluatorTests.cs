using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="OllamaCodeEvaluator"/> covering prompt
/// generation, response parsing, and failure handling against a mocked
/// Ollama HTTP endpoint.
/// </summary>
public class OllamaCodeEvaluatorTests
{
    /// <summary>HttpMessageHandler stub that records the request and returns a fixed chat response body.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;
        private string? _requestBody;

        public StubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        /// <summary>The JSON request body last sent to Ollama, for wire-contract assertions.</summary>
        public string RequestBody => _requestBody ?? throw new InvalidOperationException("No request was sent");

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return _respond();
        }
    }

    private static HttpResponseMessage ChatResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { message = new { role = "assistant", content } }),
            System.Text.Encoding.UTF8,
            "application/json")
    };

    /// <summary>Builds the evaluator over a stubbed HTTP client with default config.</summary>
    private static OllamaCodeEvaluator CreateEvaluator(StubHandler handler) =>
        new(new HttpClient(handler), new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:BaseUrl"] = "http://localhost:11434",
                ["Ollama:Model"] = "llama3.2:3b"
            })
            .Build());

    private static string SampleJson => """
        {
          "correctness": { "score": 8, "feedback": "Correct approach with edge cases handled." },
          "timeComplexity": { "bigO": "O(n)", "isOptimal": true, "feedback": "Single pass." },
          "spaceComplexity": { "bigO": "O(1)", "isOptimal": true },
          "codeStyle": { "score": 7, "feedback": "Clear names, minor redundancy." }
        }
        """;

    [Fact]
    public void BuildPrompt_ContainsCodeAndLanguage()
    {
        var (system, user) = OllamaCodeEvaluator.BuildPrompt("return a + b;", "Python");

        Assert.Contains("programming instructor", system);
        Assert.Contains("correctness", system);
        Assert.Contains("timeComplexity", system);
        Assert.Contains("spaceComplexity", system);
        Assert.Contains("codeStyle", system);
        Assert.Contains("Python", user);
        Assert.Contains("return a + b;", user);
    }

    /// <summary>
    /// Regression test: the request must serialize with camelCase keys so the
    /// format schema matches the camelCase names ParseEvaluation expects.
    /// A shared snake_case policy would rewrite timeComplexity/bigO to
    /// time_complexity/big_o and silently break every real evaluation.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_RequestSchemaUsesCamelCaseKeys()
    {
        var handler = new StubHandler(() => ChatResponse(SampleJson));
        var evaluator = CreateEvaluator(handler);

        await evaluator.EvaluateAsync("return a + b;", "Python");

        using var request = JsonDocument.Parse(handler.RequestBody);
        var root = request.RootElement;

        // Envelope
        Assert.Equal("llama3.2:3b", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(0, root.GetProperty("options").GetProperty("temperature").GetInt32());
        Assert.Equal(2, root.GetProperty("messages").GetArrayLength());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("user", root.GetProperty("messages")[1].GetProperty("role").GetString());

        // Schema keys must stay camelCase (serialization + parse contract)
        var schemaProperties = root.GetProperty("format").GetProperty("properties");
        Assert.Equal(
            new[] { "correctness", "timeComplexity", "spaceComplexity", "codeStyle" },
            schemaProperties.EnumerateObject().Select(p => p.Name));
        var complexityProperties = schemaProperties
            .GetProperty("timeComplexity").GetProperty("properties");
        Assert.Equal(
            new[] { "bigO", "isOptimal", "feedback" },
            complexityProperties.EnumerateObject().Select(p => p.Name));

        // required arrays must reference existing (camelCase) property names
        var required = root.GetProperty("format").GetProperty("required");
        Assert.Equal(
            new[] { "correctness", "timeComplexity", "spaceComplexity", "codeStyle" },
            required.EnumerateArray().Select(v => v.GetString()));
        var innerRequired = schemaProperties
            .GetProperty("timeComplexity").GetProperty("required");
        Assert.Equal(
            new[] { "bigO", "isOptimal" },
            innerRequired.EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task EvaluateAsync_ParsesAllFourDimensions()
    {
        var evaluator = CreateEvaluator(new StubHandler(() => ChatResponse(SampleJson)));

        var evaluation = await evaluator.EvaluateAsync("return a + b;", "Python");

        Assert.Equal(8, evaluation.Correctness.Score);
        Assert.Equal(10, evaluation.Correctness.MaxScore);
        Assert.Equal("Correct approach with edge cases handled.", evaluation.Correctness.Feedback);
        Assert.Equal("O(n)", evaluation.TimeComplexity.BigO);
        Assert.True(evaluation.TimeComplexity.IsOptimal);
        Assert.Equal("Single pass.", evaluation.TimeComplexity.Feedback);
        Assert.Equal("O(1)", evaluation.SpaceComplexity.BigO);
        Assert.True(evaluation.SpaceComplexity.IsOptimal);
        Assert.Null(evaluation.SpaceComplexity.Feedback);
        Assert.Equal(7, evaluation.CodeStyle.Score);
        Assert.Equal("Clear names, minor redundancy.", evaluation.CodeStyle.Feedback);
    }

    [Fact]
    public async Task EvaluateAsync_MissingDimension_FallsBackToDefaults()
    {
        var partial = """
            {
              "correctness": { "score": 5, "feedback": "Mostly works." },
              "timeComplexity": { "bigO": "O(n^2)", "isOptimal": false }
            }
            """;
        var evaluator = CreateEvaluator(new StubHandler(() => ChatResponse(partial)));

        var evaluation = await evaluator.EvaluateAsync("print('hi')", "Python");

        Assert.Equal(5, evaluation.Correctness.Score);
        Assert.Equal("O(n^2)", evaluation.TimeComplexity.BigO);
        Assert.False(evaluation.TimeComplexity.IsOptimal);
        Assert.Equal(string.Empty, evaluation.SpaceComplexity.BigO);
        Assert.False(evaluation.SpaceComplexity.IsOptimal);
        Assert.Equal(1, evaluation.CodeStyle.Score);
        Assert.Equal(string.Empty, evaluation.CodeStyle.Feedback);
    }

    [Fact]
    public async Task EvaluateAsync_MalformedJson_ThrowsInvalidOperationException()
    {
        var evaluator = CreateEvaluator(new StubHandler(() => ChatResponse("not json at all")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => evaluator.EvaluateAsync("print('hi')", "Python"));
    }

    /// <summary>
    /// The model may emit fractional scores despite the integer schema;
    /// the documented contract says malformed dimensions fall back safely
    /// instead of throwing FormatException.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_FractionalScore_FallsBackToDefault()
    {
        var fractional = """
            {
              "correctness": { "score": 8.5, "feedback": "Good." },
              "timeComplexity": { "bigO": "O(n)", "isOptimal": true },
              "spaceComplexity": { "bigO": "O(1)", "isOptimal": true },
              "codeStyle": { "score": 7, "feedback": "Fine." }
            }
            """;
        var evaluator = CreateEvaluator(new StubHandler(() => ChatResponse(fractional)));

        var evaluation = await evaluator.EvaluateAsync("print('hi')", "Python");

        Assert.Equal(1, evaluation.Correctness.Score);
        Assert.Equal("Good.", evaluation.Correctness.Feedback);
        Assert.Equal(7, evaluation.CodeStyle.Score);
    }

    /// <summary>Out-of-range scores are clamped into the documented 1-10 scale.</summary>
    [Fact]
    public async Task EvaluateAsync_OutOfRangeScore_IsClamped()
    {
        var outOfRange = """
            {
              "correctness": { "score": 15, "feedback": "Overly enthusiastic." },
              "timeComplexity": { "bigO": "O(n)", "isOptimal": true },
              "spaceComplexity": { "bigO": "O(1)", "isOptimal": true },
              "codeStyle": { "score": -2, "feedback": "Harsh." }
            }
            """;
        var evaluator = CreateEvaluator(new StubHandler(() => ChatResponse(outOfRange)));

        var evaluation = await evaluator.EvaluateAsync("print('hi')", "Python");

        Assert.Equal(10, evaluation.Correctness.Score);
        Assert.Equal(1, evaluation.CodeStyle.Score);
    }

    [Fact]
    public async Task EvaluateAsync_EmptyContent_ThrowsInvalidOperationException()
    {
        var emptyBody = JsonSerializer.Serialize(
            new { message = new { role = "assistant", content = "" } });
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(emptyBody, System.Text.Encoding.UTF8, "application/json")
        });
        var evaluator = CreateEvaluator(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => evaluator.EvaluateAsync("print('hi')", "Python"));
    }
}
