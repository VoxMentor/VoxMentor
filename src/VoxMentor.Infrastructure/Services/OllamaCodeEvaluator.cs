using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Evaluates code via a local Ollama instance (<c>/api/chat</c>) using a
/// structured-output JSON schema so the model always returns the four
/// evaluation dimensions in a parseable shape.
/// </summary>
public class OllamaCodeEvaluator : ICodeEvaluator
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public OllamaCodeEvaluator(HttpClient http, IConfiguration config)
    {
        _http = http;
        _baseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = config["Ollama:Model"] ?? "llama3.2:3b";
    }

    public async Task<CodeEvaluation> EvaluateAsync(string code, string language, CancellationToken cancellationToken = default)
    {
        var (systemPrompt, userPrompt) = BuildPrompt(code, language);

        var request = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            stream = false,
            format = Schema,
            options = new { temperature = 0 }
        };

        var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/chat", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var chat = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(JsonOptions, cancellationToken);

        if (string.IsNullOrWhiteSpace(chat?.Message?.Content))
            throw new InvalidOperationException("Ollama returned an empty evaluation response");

        return ParseEvaluation(chat.Message.Content);
    }

    /// <summary>
    /// Builds the system and user prompts asking the model to score the code
    /// on the four dimensions defined by the response schema.
    /// </summary>
    public static (string System, string User) BuildPrompt(string code, string language)
    {
        var system = """
            You are a strict programming instructor evaluating a student's solution.
            Score the code on four dimensions:
            - correctness (1-10): logical correctness, edge cases, algorithm validity.
            - timeComplexity: Big-O of the runtime, and whether it is optimal.
            - spaceComplexity: Big-O of the memory usage, and whether it is optimal.
            - codeStyle (1-10): naming, structure, readability, idiomatic language use.
            Respond with JSON matching the required schema. Be concise in feedback.
            """;

        var user = $"""
            Evaluate the following {language} code:

            ```
            {code}
            ```
            """;

        return (system, user);
    }

    /// <summary>
    /// Deserializes the model's JSON output into a <see cref="CodeEvaluation"/>.
    /// Invalid JSON throws <see cref="InvalidOperationException"/>; missing or
    /// malformed individual dimensions fall back to safe defaults.
    /// </summary>
    public static CodeEvaluation ParseEvaluation(string content)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Ollama evaluation response is not valid JSON", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Ollama evaluation response is not a JSON object");

            return new CodeEvaluation
            {
                Correctness = ToDimension(root, "correctness", defaultScore: 1),
                TimeComplexity = ToComplexity(root, "timeComplexity"),
                SpaceComplexity = ToComplexity(root, "spaceComplexity"),
                CodeStyle = ToDimension(root, "codeStyle", defaultScore: 1)
            };
        }
    }

    private static DimensionScore ToDimension(JsonElement content, string name, int defaultScore)
    {
        if (!content.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Object)
            return new DimensionScore(defaultScore, 10, string.Empty);

        var score = node.TryGetProperty("score", out var scoreNode) && scoreNode.ValueKind == JsonValueKind.Number
            ? scoreNode.GetInt32()
            : defaultScore;
        var feedback = node.TryGetProperty("feedback", out var feedbackNode) && feedbackNode.ValueKind == JsonValueKind.String
            ? feedbackNode.GetString() ?? string.Empty
            : string.Empty;

        return new DimensionScore(score, 10, feedback);
    }

    private static ComplexityScore ToComplexity(JsonElement content, string name)
    {
        if (!content.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Object)
            return new ComplexityScore(string.Empty, false);

        var bigO = node.TryGetProperty("bigO", out var bigONode) && bigONode.ValueKind == JsonValueKind.String
            ? bigONode.GetString() ?? string.Empty
            : string.Empty;
        var isOptimal = node.TryGetProperty("isOptimal", out var optimalNode) && optimalNode.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? optimalNode.GetBoolean()
            : false;
        var feedback = node.TryGetProperty("feedback", out var feedbackNode) && feedbackNode.ValueKind == JsonValueKind.String
            ? feedbackNode.GetString()
            : null;

        return new ComplexityScore(bigO, isOptimal, feedback);
    }

    /// <summary>Structured-output JSON schema enforcing the four evaluation dimensions.</summary>
    private static object Schema => new
    {
        type = "object",
        properties = new
        {
            correctness = new
            {
                type = "object",
                properties = new
                {
                    score = new { type = "integer", minimum = 1, maximum = 10 },
                    feedback = new { type = "string" }
                },
                required = new[] { "score", "feedback" }
            },
            timeComplexity = new
            {
                type = "object",
                properties = new
                {
                    bigO = new { type = "string" },
                    isOptimal = new { type = "boolean" },
                    feedback = new { type = "string" }
                },
                required = new[] { "bigO", "isOptimal" }
            },
            spaceComplexity = new
            {
                type = "object",
                properties = new
                {
                    bigO = new { type = "string" },
                    isOptimal = new { type = "boolean" },
                    feedback = new { type = "string" }
                },
                required = new[] { "bigO", "isOptimal" }
            },
            codeStyle = new
            {
                type = "object",
                properties = new
                {
                    score = new { type = "integer", minimum = 1, maximum = 10 },
                    feedback = new { type = "string" }
                },
                required = new[] { "score", "feedback" }
            }
        },
        required = new[] { "correctness", "timeComplexity", "spaceComplexity", "codeStyle" }
    };
}

/// <summary>Wire shape of an Ollama /api/chat non-streaming response.</summary>
public class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaChatMessage? Message { get; set; }
}

/// <summary>Assistant message returned by Ollama.</summary>
public class OllamaChatMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
