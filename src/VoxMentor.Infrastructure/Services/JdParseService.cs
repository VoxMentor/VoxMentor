using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Parses a job description into structured skill weights via a local Ollama
/// instance (#117). Uses the structured-output <c>format</c> schema with
/// temperature 0 so the model returns a parseable shape; any failure
/// (unreachable, non-2xx, empty, unparseable) returns null and the caller
/// degrades to <c>JdHeuristics</c> — never a 503.
/// </summary>
public class JdParseService : IJdParseService
{
    private readonly HttpClient _http;
    private readonly ILogger<JdParseService> _logger;
    private readonly string _baseUrl;
    private readonly string _model;

    /// <summary>Bounds the call so a hung Ollama cannot stall the upload request.</summary>
    private static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public JdParseService(HttpClient http, IConfiguration config, ILogger<JdParseService> logger)
    {
        _http = http;
        _logger = logger;
        _baseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = config["Ollama:Model"] ?? "llama3.2:3b";
    }

    /// <summary>Returns null (with a warning logged) on any failure — degrade, don't fail.</summary>
    public async Task<JdParseResult?> ParseAsync(string rawText, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = rawText }
            },
            stream = false,
            format = Schema,
            options = new { temperature = 0 }
        };

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ParseTimeout);

            using var response = await _http.PostAsJsonAsync(
                $"{_baseUrl}/api/chat", request, RequestJsonOptions, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ollama HTTP {StatusCode} for JD parse; falling back to heuristics",
                    response.StatusCode);
                return null;
            }

            // Reuses the shared /api/chat wire DTO from OllamaCodeEvaluator —
            // one contract for the Ollama response shape, not two copies.
            var chat = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                ResponseJsonOptions, timeoutCts.Token);

            if (string.IsNullOrWhiteSpace(chat?.Message?.Content))
            {
                _logger.LogWarning("Ollama returned an empty JD parse; falling back to heuristics");
                return null;
            }

            return ParseResult(chat.Message.Content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the CALLER aborted the request — not ours to swallow
        }
        catch (OperationCanceledException)
        {
            // Our internal 60s cap fired (timeoutCts), caller token still live:
            // this is the "Ollama is slow" case — degrade, never 500 (#117).
            _logger.LogWarning("Ollama JD parse timed out; falling back to heuristics");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "JD parse via Ollama failed; falling back to heuristics");
            return null;
        }
    }

    /// <summary>
    /// Deserializes the model's JSON into a <see cref="JdParseResult"/>.
    /// Malformed shapes throw <see cref="InvalidOperationException"/> — the
    /// caller treats that the same as Ollama being down.
    /// </summary>
    public static JdParseResult ParseResult(string content)
    {
        var parsed = JsonSerializer.Deserialize<JdParseResult>(content, ResponseJsonOptions)
                     ?? throw new InvalidOperationException("Ollama JD response deserialized to null");

        if (parsed.CompanyName is null) parsed.CompanyName = "Unknown";
        if (parsed.Role is null) parsed.Role = "Unknown";
        if (parsed.Difficulty is null) parsed.Difficulty = "Medium";
        if (parsed.TechnicalSkills is null)
            parsed.TechnicalSkills = new Dictionary<string, float>(StringComparer.Ordinal);
        if (parsed.HrSkills is null)
            parsed.HrSkills = new Dictionary<string, float>(StringComparer.Ordinal);
        // ponytail: no clamping here — JdWeightNormalizer drops out-of-range
        // values downstream, single owner for weight hygiene.
        if (parsed.EstimatedWeeks <= 0)
            parsed.EstimatedWeeks = 6;

        return parsed;
    }

    private const string SystemPrompt =
        "You extract structured hiring requirements from a job description. " +
        "Respond with JSON only: companyName (string), role (string), " +
        "technicalSkills (object mapping programming/CS topic names to relative weights), " +
        "hrSkills (object mapping behavioral competency names to relative weights), " +
        "difficulty (string: Easy, Medium, or Hard), estimatedWeeks (integer, weeks to prepare). " +
        "Weights are relative importance within each group; they do not need to sum to 1. " +
        "Use concise canonical topic names like Arrays, DP, Graphs, Trees, System Design.";

    /// <summary>Structured-output schema so Ollama always returns a parseable shape.</summary>
    private static object Schema => new
    {
        type = "object",
        properties = new
        {
            companyName = new { type = "string" },
            role = new { type = "string" },
            technicalSkills = new
            {
                type = "object",
                additionalProperties = new { type = "number" }
            },
            hrSkills = new
            {
                type = "object",
                additionalProperties = new { type = "number" }
            },
            difficulty = new { type = "string" },
            estimatedWeeks = new { type = "integer" }
        },
        required = new[] { "companyName", "role", "technicalSkills", "hrSkills", "difficulty", "estimatedWeeks" }
    };
}
