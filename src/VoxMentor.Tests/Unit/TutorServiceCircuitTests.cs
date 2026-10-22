using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Infrastructure.Persistence;
using VoxMentor.Infrastructure.Plagiarism;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Circuit recording at the full-stream boundary (#72): HTTP failures, NDJSON
/// error lines, and EOF without done=true all count as failures; only a
/// complete stream records success; an open breaker short-circuits before any
/// Ollama call.
/// </summary>
public class TutorServiceCircuitTests
{
    /// <summary>Circuit stub that counts success and failure recordings.</summary>
    private sealed class RecordingCircuit : ITutorCircuit
    {
        /// <summary>Whether the circuit reports as open.</summary>
        public bool IsOpen { get; set; }
        /// <summary>Number of recorded successes.</summary>
        public int Successes { get; private set; }
        /// <summary>Number of recorded failures.</summary>
        public int Failures { get; private set; }

        /// <summary>Increments the success counter.</summary>
        public void RecordSuccess() => Successes++;

        /// <summary>Increments the failure counter.</summary>
        public void RecordFailure() => Failures++;
    }

    /// <summary>HTTP handler stub that answers via a factory and counts calls.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        /// <summary>Number of HTTP requests received.</summary>
        public int Calls { get; private set; }

        /// <summary>Creates a handler that answers each request with the given factory.</summary>
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            => _respond = respond;

        /// <summary>Counts the call and returns the factory-built response.</summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_respond(request));
        }
    }

    /// <summary>Creates an HTTP 200 response whose body is the given NDJSON text.</summary>
    private static HttpResponseMessage NdJson(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    /// <summary>Builds a TutorService wired to stubbed HTTP handlers, an InMemory DbContext, and a recording circuit.</summary>
    private static (TutorService Tutor, RecordingCircuit Circuit, StubHandler Chat) CreateTutor(
        Func<HttpRequestMessage, HttpResponseMessage> respondChat)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Ollama:BaseUrl"] = "http://ollama.test" }).Build();

        // Embedding must degrade to null (HTTP 500), otherwise retrieval would
        // run raw pgvector SQL against the InMemory provider.
        var embedder = new CodeEmbeddingService(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))),
            config,
            NullLogger<CodeEmbeddingService>.Instance);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options, new TestCurrentUser());

        var chat = new StubHandler(respondChat);
        var circuit = new RecordingCircuit();
        var tutor = new TutorService(
            new HttpClient(chat), config, embedder, db,
            NullLogger<TutorService>.Instance, circuit);
        return (tutor, circuit, chat);
    }

    /// <summary>Fully drains an async chunk stream.</summary>
    private static async Task ConsumeAsync(IAsyncEnumerable<TutorChunk> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    /// <summary>A complete NDJSON stream ending in done=true records exactly one success and no failures.</summary>
    [Fact]
    public async Task Stream_CompleteStreamWithFinalChunk_RecordsSuccess()
    {
        var (tutor, circuit, _) = CreateTutor(_ => NdJson(
            """{"message":{"content":"Kadane "},"done":false}""" + "\n" +
            """{"message":{"content":"is O(n)."},"done":false}""" + "\n" +
            """{"message":{"content":""},"done":true,"eval_count":42}""" + "\n"));

        await ConsumeAsync(tutor.StreamAnswerAsync("Why Kadane?", null));

        Assert.Equal(1, circuit.Successes);
        Assert.Equal(0, circuit.Failures);
    }

    /// <summary>An HTTP error status throws and records one circuit failure.</summary>
    [Fact]
    public async Task Stream_HttpError_RecordsFailure()
    {
        var (tutor, circuit, _) = CreateTutor(
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ConsumeAsync(tutor.StreamAnswerAsync("q", null)));

        Assert.Equal(0, circuit.Successes);
        Assert.Equal(1, circuit.Failures);
    }

    /// <summary>An NDJSON error line after HTTP 200 still throws and records one circuit failure.</summary>
    [Fact]
    public async Task Stream_NdjsonErrorLineAfterHttp200_RecordsFailure()
    {
        // HTTP 200 + mid-stream error object: the failure happens while reading
        // the body, outside any HTTP header check — it must still count.
        var (tutor, circuit, _) = CreateTutor(_ => NdJson(
            """{"message":{"content":"partial"},"done":false}""" + "\n" +
            """{"error":"model crashed"}""" + "\n"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ConsumeAsync(tutor.StreamAnswerAsync("q", null)));

        Assert.Equal(0, circuit.Successes);
        Assert.Equal(1, circuit.Failures);
    }

    /// <summary>EOF without a final done=true chunk records one circuit failure and no success.</summary>
    [Fact]
    public async Task Stream_EofWithoutFinalChunk_RecordsFailure()
    {
        var (tutor, circuit, _) = CreateTutor(_ => NdJson(
            """{"message":{"content":"truncated"},"done":false}""" + "\n"));

        await ConsumeAsync(tutor.StreamAnswerAsync("q", null));

        Assert.Equal(0, circuit.Successes);
        Assert.Equal(1, circuit.Failures);
    }

    /// <summary>An open circuit throws before any Ollama HTTP call is made and records nothing.</summary>
    [Fact]
    public async Task Stream_CircuitOpen_ShortCircuitsWithoutCallingOllama()
    {
        var (tutor, circuit, chat) = CreateTutor(_ => NdJson(
            """{"message":{"content":""},"done":true,"eval_count":1}""" + "\n"));
        circuit.IsOpen = true;

        var enumerator = tutor.StreamAnswerAsync("q", null).GetAsyncEnumerator();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await enumerator.MoveNextAsync());

        Assert.Equal(0, chat.Calls);
        Assert.Equal(0, circuit.Successes);
        Assert.Equal(0, circuit.Failures);
    }
}
