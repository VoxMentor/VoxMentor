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
/// Minimal RAG pipeline for the AI Tutor (#73): embed the question, take the
/// top-Rag:TopK textbook chunks by pgvector cosine similarity, render the
/// shared excerpt prompt, and stream Ollama's NDJSON output. Issue #72 layers
/// the Hangfire worker and circuit breaker on top of this.
/// </summary>
public class TutorService : ITutorService
{
    private readonly HttpClient _http;
    private readonly CodeEmbeddingService _embeddingService;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<TutorService> _logger;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly int _topK;

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
        // #77: Rag:TopK replaces the hardcoded LIMIT 5 so the eval can tune k.
        _topK = int.TryParse(config["Rag:TopK"], out var topK) ? Math.Clamp(topK, 1, 50) : 5;
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

        // ponytail: HttpClient.Timeout stops at headers with ResponseHeadersRead —
        // this linked CTS caps header wait + body read together (300s total per ask,
        // matches client.Timeout in DependencyInjection.cs). A stalled Ollama mid-stream
        // now cancels instead of pinning the user's InFlightAsks entry forever (#89 F2).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(300));

        using var response = await _http.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Ollama HTTP {StatusCode} for tutor stream: {Reason}", response.StatusCode, response.ReasonPhrase);
            throw new InvalidOperationException("Tutor service temporarily unavailable.");
        }

        await using var body = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
        await foreach (var chunk in ReadStreamAsync(body, timeoutCts.Token))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Top-Rag:TopK chunks by cosine distance, concept-filtered when a concept
    /// is given. Returns an empty list when the question can't be embedded
    /// (Ollama down) — generation then proceeds without excerpts instead of
    /// failing the session. Per-query retrieval metrics (k, concept, chunk
    /// distances, elapsed) are logged for the #77 evaluation.
    /// </summary>
    private async Task<List<RetrievedChunk>> RetrieveAsync(string question, Guid? conceptId, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var embedding = await _embeddingService.EmbedAsync(question, cancellationToken);
        if (embedding is null)
        {
            _logger.LogWarning("Embedding service returned null for question (conceptId={ConceptId}); proceeding without textbook context",
                conceptId);
            return new List<RetrievedChunk>();
        }

        var vector = new Pgvector.Vector(embedding);

        // ponytail: raw SQL for pgvector cosine — EF Core can't translate <=>.
        // #77: SELECT carries Distance so retrieval quality can be logged/eval'd.
        var vecRef = conceptId.HasValue ? "{1}" : "{0}";
        var sql = $"""
            SELECT "Id", "Content", "Source",
                   "Embedding"::vector <=> {vecRef}::vector AS "Distance"
            FROM "TextbookChunks"
            WHERE "Embedding" IS NOT NULL
            """;
        if (conceptId.HasValue)
        {
            sql += "\n  AND \"ConceptId\" = {0}";
        }

        sql += $"""

            ORDER BY "Embedding"::vector <=> {vecRef}::vector
            LIMIT {_topK}
            """;

        var parameters = conceptId.HasValue
            ? new object[] { conceptId.Value, vector }
            : new object[] { vector };

        // CodeRabbit #96: HNSW explores ~ef_search candidates; the ConceptId
        // post-filter can exhaust that budget before LIMIT TopK fills. Raise it
        // for this query and let iterative_scan continue past filtered-out rows
        // (pgvector >= 0.8 for iterative_scan; enforced via /health pgvector check).
        // Raw: Postgres SET takes no bind parameters; the value is an int.
        var efSearch = Math.Max(40, _topK * 2);
        // IApplicationDbContext is the Application-layer abstraction; the GUC
        // SETs need the concrete EF DatabaseFacade (prod DI: ApplicationDbContext).
        var efDb = (DbContext)_db;
        await using var transaction = await efDb.Database.BeginTransactionAsync(cancellationToken);
        // concatenated, not interpolated: SET takes no bind params, and an int
        // formatted invariant is injection-proof (EF1002 otherwise)
        await efDb.Database.ExecuteSqlRawAsync(
            "SET LOCAL hnsw.ef_search = " + efSearch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            cancellationToken);
        await efDb.Database.ExecuteSqlRawAsync("SET LOCAL hnsw.iterative_scan = 'strict_order'", cancellationToken);
        var chunks = await _db.SqlQueryRaw<RetrievedChunk>(sql, parameters)
            .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        stopwatch.Stop();
        _logger.LogInformation(
            "Tutor retrieval k={TopK} conceptId={ConceptId} chunks={ChunkCount} distances=[{Distances}] elapsedMs={ElapsedMs}",
            _topK,
            conceptId,
            chunks.Count,
            string.Join(",", chunks.Select(c =>
                c.Distance?.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a")),
            stopwatch.ElapsedMilliseconds);
        return chunks;
    }

    /// <summary>
    /// Renders prompts/tutor-prompt.txt — the embedded template is the single
    /// source of truth shared verbatim with scripts/eval-tutor.py (#77).
    /// Prompt decisions documented per the issue: numbered [n] citations bind
    /// every claim to a retrieved chunk so citation accuracy is machine-checkable;
    /// the model must say the excerpts lack the information instead of guessing
    /// (anti-hallucination); each excerpt carries its Source so expected-source
    /// matching in the eval is possible; untrusted fields (question, chunk
    /// content, source) are wrapped in &lt;excerpts&gt;/&lt;question&gt;
    /// delimiters and escaped so they cannot inject instructions (CWE-1427).
    /// Do not edit the C# side only — the
    /// template file drives both prod and eval.
    /// </summary>
    public static string BuildPrompt(string question, IReadOnlyList<RetrievedChunk> chunks)
    {
        var text = PromptTemplate.Value;
        if (chunks.Count == 0)
        {
            // drop the {EXCERPTS} placeholder line entirely (keeps the
            // no-excerpts contract: no "Excerpts:" header, just the question)
            var marker = text.IndexOf("{EXCERPTS}", StringComparison.Ordinal);
            if (marker >= 0)
            {
                var end = marker + "{EXCERPTS}".Length;
                if (end < text.Length && text[end] == '\n')
                {
                    end++;
                }
                text = text.Remove(marker, end - marker);
            }
        }
        else
        {
            // explicit '\n' (not AppendLine): prompt bytes must match the
            // Python evaluator's rendering on every OS
            var sb = new StringBuilder();
            sb.Append("<excerpts>\nExcerpts:\n");
            for (var i = 0; i < chunks.Count; i++)
            {
                sb.Append('[').Append(i + 1).Append("] ")
                    .Append(EscapeUntrusted(chunks[i].Content)).Append('\n');
                sb.Append("    Source: ").Append(EscapeUntrusted(chunks[i].Source));
                if (i < chunks.Count - 1)
                {
                    sb.Append('\n');
                }
            }
            sb.Append("\n</excerpts>");
            text = text.Replace("{EXCERPTS}", sb.ToString());
        }

        return text.Replace("{QUESTION}", "<question>" + EscapeUntrusted(question) + "</question>");
    }

    /// <summary>
    /// Escapes a literal "&lt;/" so untrusted text (question, chunk content,
    /// source names) cannot close the &lt;excerpts&gt;/&lt;question&gt;
    /// delimiters early (CWE-1427, CodeRabbit #96). Mirrored by
    /// render_prompt in scripts/eval-tutor.py — keep both in sync.
    /// </summary>
    private static string EscapeUntrusted(string value) => value.Replace("</", "<\\/");

    private static readonly Lazy<string> PromptTemplate = new(LoadPromptTemplate);

    private static string LoadPromptTemplate()
    {
        const string resourceName = "VoxMentor.Infrastructure.tutor-prompt.txt";
        using var stream = typeof(TutorService).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded prompt template '{resourceName}' not found — check the EmbeddedResource in VoxMentor.Infrastructure.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n');
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

    /// <summary>Keyless DTO for the top-chunk retrieval query; Distance is the
    /// pgvector cosine distance (0 = identical), logged as a retrieval metric (#77).</summary>
    public sealed record RetrievedChunk(Guid Id, string Content, string Source, double? Distance = null);
}
