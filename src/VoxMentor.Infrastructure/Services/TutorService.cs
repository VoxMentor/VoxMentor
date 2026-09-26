using System.Runtime.CompilerServices;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Infrastructure.Plagiarism;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Minimal RAG pipeline for the AI tutor (#73): embed the question, take the
/// top-5 textbook chunks by pgvector cosine similarity, build the excerpt
/// prompt, and stream Ollama's NDJSON output. Issue #72 layers the Hangfire
/// worker and circuit breaker on top of this.
/// </summary>
public class TutorService : ITutorService
{
    private readonly HttpClient _http;
    private readonly CodeEmbeddingService _embeddingService;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<TutorService> _logger;
    private readonly string _baseUrl;
    private readonly string _model;

    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web);

    public TutorService(
        HttpClient http,
        IConfiguration config,
        CodeEmbeddingService embeddingService,
        IApplicationDbContext db,
        ILogger<TutorService> logger)
    {
        _http = http;
        _embeddingService = embeddingService;
        _db = db;
        _logger = logger;
        _baseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = config["Ollama:Model"] ?? "llama3.2:3b";
    }

    public async IAsyncEnumerable<TutorChunk> StreamAnswerAsync(
        string question,
        Guid? conceptId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chunks = await RetrieveAsync(question, conceptId, cancellationToken);
        if (chunks.Count == 0)
        {
            _logger.LogDebug("No textbook chunks retrieved for question (concept {ConceptId})", conceptId);
        }

        var request = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "user", content = BuildPrompt(question, chunks) }
            },
            stream = true
        };

        var json = JsonSerializer.Serialize(request, RequestJsonOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var response = await _http.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Ollama HTTP {StatusCode} for tutor stream: {Reason}", response.StatusCode, response.ReasonPhrase);
            throw new InvalidOperationException("Tutor service temporarily unavailable.");
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var chunk in ReadStreamAsync(body, cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Top-5 chunks by cosine distance, concept-filtered when a concept is given.
    /// Returns an empty list when the question can't be embedded (Ollama down) —
    /// generation then proceeds without excerpts instead of failing the session.
    /// </summary>
    private async Task<List<RetrievedChunk>> RetrieveAsync(string question, Guid? conceptId, CancellationToken cancellationToken)
    {
        var embedding = await _embeddingService.EmbedAsync(question, cancellationToken);
        if (embedding is null)
        {
            _logger.LogWarning("Embedding service returned null for question (conceptId={ConceptId}); proceeding without textbook context",
                conceptId);
            return new List<RetrievedChunk>();
        }

        var vector = new Pgvector.Vector(embedding);

        // ponytail: raw SQL for pgvector cosine — EF Core can't translate <=>.
        const string select = """
            SELECT "Id", "Content", "Source"
            FROM "TextbookChunks"
            WHERE "Embedding" IS NOT NULL
            """;
        const string orderBy = """
            ORDER BY "Embedding"::vector <=> {0}::vector
            LIMIT 5
            """;

        var sql = conceptId.HasValue
            ? $"{select}\n  AND \"ConceptId\" = {{0}}\n{orderBy.Replace("{0}", "{1}")}"
            : $@"{select}
{orderBy}";

        var parameters = conceptId.HasValue
            ? new object[] { conceptId.Value, vector }
            : new object[] { vector };

        return await _db.SqlQueryRaw<RetrievedChunk>(sql, parameters)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Prompt template from issue #72: retrieved excerpts with citations, then
    /// the student's question.
    /// </summary>
    public static string BuildPrompt(string question, IReadOnlyList<RetrievedChunk> chunks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Based on these textbook excerpts, answer the student's question.");
        sb.AppendLine("Cite the source material where relevant.");
        sb.AppendLine();
        if (chunks.Count > 0)
        {
            sb.AppendLine("Excerpts:");
            for (var i = 0; i < chunks.Count; i++)
            {
                sb.AppendLine($"{chunks[i].Content} [Source: {chunks[i].Source}]");
            }
            sb.AppendLine();
        }
        sb.AppendLine($"Question: {question}");
        sb.AppendLine();
        sb.Append("Answer:");
        return sb.ToString();
    }

    /// <summary>
    /// Reads Ollama's application/x-ndjson body line by line. Yields one
    /// <see cref="TutorChunk"/> per content token; the final chunk (done=true)
    /// carries eval_count. Ollama reports mid-stream failures as
    /// {"error":"..."} with HTTP 200 — those throw.
    /// </summary>
    public static async IAsyncEnumerable<TutorChunk> ReadStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var chunk = ParseLine(line);
            if (chunk is not null)
            {
                yield return chunk;
            }
        }
    }

    /// <summary>
    /// Parses one NDJSON line. Returns null for blank or contentless
    /// non-final lines; throws when the line carries an Ollama error object.
    /// </summary>
    public static TutorChunk? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        OllamaStreamEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<OllamaStreamEvent>(line, StreamJsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Ollama stream line is not valid JSON", ex);
        }

        if (evt?.Error is { Length: > 0 } error)
        {
            throw new InvalidOperationException("Tutor service temporarily unavailable.");
        }

        var text = evt?.Message?.Content;
        var done = evt?.Done ?? false;
        if (!done && string.IsNullOrEmpty(text))
        {
            return null;
        }

        return new TutorChunk(text ?? string.Empty, done, evt?.EvalCount);
    }

    private static readonly JsonSerializerOptions StreamJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Wire shape of one Ollama /api/chat streaming line.</summary>
    public sealed class OllamaStreamEvent
    {
        [JsonPropertyName("message")]
        public OllamaChatMessage? Message { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    /// <summary>Keyless DTO for the top-chunk retrieval query.</summary>
    public sealed record RetrievedChunk(Guid Id, string Content, string Source);
}
