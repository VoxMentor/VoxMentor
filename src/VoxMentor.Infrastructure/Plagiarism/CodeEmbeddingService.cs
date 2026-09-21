using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace VoxMentor.Infrastructure.Plagiarism;

/// <summary>
/// Generates 768-dim code embeddings via Ollama's <c>/api/embed</c> endpoint
/// using the <c>nomic-embed-text</c> model. Follows the same HttpClient
/// pattern as <see cref="Services.OllamaCodeEvaluator"/>.
/// </summary>
public class CodeEmbeddingService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly ILogger<CodeEmbeddingService> _logger;

    private static readonly TimeSpan EmbedTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CodeEmbeddingService(HttpClient http, IConfiguration config, ILogger<CodeEmbeddingService> logger)
    {
        _http = http;
        _logger = logger;
        _baseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = config["Ollama:EmbedModel"] ?? "nomic-embed-text";
    }

    /// <summary>
    /// Generates a 768-dim embedding vector for the given source code.
    /// Returns <c>null</c> if Ollama is unavailable or returns an invalid response.
    /// </summary>
    public async Task<float[]?> EmbedAsync(string code, CancellationToken cancellationToken = default)
    {
        // ponytail: truncate to 8000 chars to avoid slow embeddings on huge files
        var truncated = code.Length > 8000 ? code[..8000] : code;

        // ponytail: dimension must stay at 768 — changing the model resets all stored embeddings.
        // If model changes, run a full re-embed migration before deploying.
        var request = new
        {
            model = _model,
            input = truncated
        };

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(EmbedTimeout);

            var response = await _http.PostAsJsonAsync(
                $"{_baseUrl}/api/embed", request, JsonOptions, timeoutCts.Token);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<EmbedResponse>(JsonOptions, timeoutCts.Token);

            if (result?.Embeddings is { Length: > 0 } embeddings &&
                embeddings[0] is { Length: 768 } emb &&
                Array.TrueForAll(emb, float.IsFinite))
                return emb;

            _logger.LogWarning("Ollama embed returned invalid result (wrong dimension or non-finite values)");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to generate embedding via Ollama");
            return null;
        }
    }

    private sealed class EmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public float[][]? Embeddings { get; set; }
    }
}
