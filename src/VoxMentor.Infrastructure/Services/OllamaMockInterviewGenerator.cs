using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.MockInterviews;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Mock-interview dialogue lines via a local Ollama instance (#124). Plain-text
/// chat completion (no schema); any failure (unreachable, non-2xx, empty,
/// timeout) returns null and the caller substitutes a canned line â€” the same
/// degrade-don't-fail contract as <see cref="JdParseService"/>.
/// </summary>
public class OllamaMockInterviewGenerator : IMockInterviewGenerator
{
    private readonly HttpClient _http;
    private readonly ILogger<OllamaMockInterviewGenerator> _logger;
    private readonly string _baseUrl;
    private readonly string _model;

    /// <summary>Bounds the call so a hung Ollama cannot stall the interview request.</summary>
    private static readonly TimeSpan GenerateTimeout = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OllamaMockInterviewGenerator(
        HttpClient http, IConfiguration config, ILogger<OllamaMockInterviewGenerator> logger)
    {
        _http = http;
        _logger = logger;
        _baseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = config["Ollama:Model"] ?? "llama3.2:3b";
    }

    /// <summary>Greets the candidate and introduces the opening problem.</summary>
    public Task<string?> OpeningAsync(
        string? company, string? role, string problemTitle, CancellationToken cancellationToken = default)
    {
        var context = company is not null && role is not null
            ? $"The interview is for the {role} position at {company}."
            : "The candidate has no job description attached; keep the greeting generic.";

        return GenerateAsync(
            "You are a friendly but professional interviewer starting a mock technical interview. " +
            "Greet the candidate in one sentence and introduce the first problem by its title in one more sentence. " +
            "Plain text only: no markdown, no numbering, no quotation marks.",
            $"{context}\nFirst problem: {problemTitle}",
            cancellationToken);
    }

    /// <summary>Asks exactly one short follow-up or probe for the latest answer.</summary>
    public Task<string?> FollowUpAsync(
        IReadOnlyList<TranscriptTurn> transcript, CancellationToken cancellationToken = default)
    {
        var conversation = string.Join(
            "\n",
            transcript.Select(t => $"{t.Role}: {t.Message}"));

        return GenerateAsync(
            "You are a technical interviewer. Read the conversation and ask exactly ONE short follow-up " +
            "question or probe that digs into the candidate's latest answer (complexity, edge cases, " +
            "trade-offs, or a hint if they are stuck). Plain text only: no markdown, max two sentences.",
            conversation,
            cancellationToken);
    }

    /// <summary>Returns null (with a warning logged) on any failure â€” degrade, don't fail.</summary>
    private async Task<string?> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            stream = false,
            options = new { temperature = 0.7 }
        };

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(GenerateTimeout);

            using var response = await _http.PostAsJsonAsync(
                $"{_baseUrl}/api/chat", request, RequestJsonOptions, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ollama HTTP {StatusCode} for mock interview line; using canned text",
                    response.StatusCode);
                return null;
            }

            // Reuses the shared /api/chat wire DTO from OllamaCodeEvaluator â€”
            // one contract for the Ollama response shape, not two copies.
            var chat = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                ResponseJsonOptions, timeoutCts.Token);

            var content = chat?.Message?.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Ollama returned an empty mock interview line; using canned text");
                return null;
            }

            return content;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the CALLER aborted the request â€” not ours to swallow
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Ollama mock interview line timed out; using canned text");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Mock interview line via Ollama failed; using canned text");
            return null;
        }
    }
}
