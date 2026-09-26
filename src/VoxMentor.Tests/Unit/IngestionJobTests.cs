using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;
using VoxMentor.Infrastructure.Plagiarism;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Ingestion job coverage: happy path, embedding failure, empty input, and
/// missing job (#70).
/// </summary>
public class IngestionJobTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;
        public int RequestCount { get; private set; }

        public StubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_respond());
        }
    }

    private static HttpResponseMessage EmbedResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                embeddings = new[] { Enumerable.Range(0, 768).Select(i => i / 1000f).ToArray() }
            }),
            Encoding.UTF8,
            "application/json")
    };

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static CodeEmbeddingService CreateEmbedder(StubHandler handler)
        => new(
            new HttpClient(handler),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Ollama:BaseUrl"] = "http://localhost:11434",
                    ["Ollama:EmbedModel"] = "nomic-embed-text"
                })
                .Build(),
            NullLogger<CodeEmbeddingService>.Instance);

    private static IngestionJob CreateJob(ApplicationDbContext db, StubHandler handler)
        => new(db, CreateEmbedder(handler), NullLogger<IngestionJob>.Instance);

    private static string TempDirFile(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), ITextbookIngestionQueue.TempDirectoryName);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    private static async Task<(TextbookJob Job, string Path)> CreateJobRowWithFileAsync(
        ApplicationDbContext db, string fileName, string content, Guid? conceptId = null)
    {
        var jobId = Guid.NewGuid();
        var path = TempDirFile($"vox-test-{jobId}.txt");
        await File.WriteAllTextAsync(path, content);

        var job = new TextbookJob
        {
            Id = jobId,
            FileName = fileName,
            ConceptId = conceptId,
            Status = TextbookJobStatus.Pending
        };
        db.TextbookJobs.Add(job);
        await db.SaveChangesAsync();
        return (job, path);
    }

    /// <summary>400 distinct words — forces two chunks (375-word window).</summary>
    private static string SampleText()
        => string.Join(' ', Enumerable.Range(0, 400).Select(i => $"word{i}"));

    [Fact]
    public async Task Execute_HappyPath_CompletesWithChunksAndDeletesTempFile()
    {
        var db = CreateDb();
        var conceptId = Guid.NewGuid();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText(), conceptId);
        var handler = new StubHandler(EmbedResponse);

        await CreateJob(db, handler)
            .Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Completed, result.Status);
        Assert.Equal(2, result.TotalChunks);
        Assert.Equal(2, result.ProcessedChunks);
        Assert.Null(result.Error);
        Assert.NotNull(result.CompletedAt);

        var chunks = await db.TextbookChunks.Where(c => c.JobId == job.Id).ToListAsync();
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c =>
        {
            Assert.NotNull(c.Embedding);
            Assert.Equal("book.txt", c.Source);
            Assert.Equal(conceptId, c.ConceptId);
            Assert.False(string.IsNullOrWhiteSpace(c.Content));
        });
        Assert.Equal(2, handler.RequestCount);

        Assert.False(File.Exists(path), "temp file should be deleted after processing");
    }

    [Fact]
    public async Task Execute_OllamaDown_FailsJob_RecordsError_DeletesChunksAndTempFile()
    {
        var db = CreateDb();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText());
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await CreateJob(db, handler).Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Failed, result.Status);
        Assert.Contains("Embedding failed", result.Error);
        Assert.Contains("chunk 1/2", result.Error);
        Assert.NotNull(result.CompletedAt);

        Assert.Empty(await db.TextbookChunks.ToListAsync());
        Assert.False(File.Exists(path), "temp file should be deleted even on failure");
    }

    [Fact]
    public async Task Execute_NoExtractableText_FailsWithClearError()
    {
        var db = CreateDb();
        var (job, path) = await CreateJobRowWithFileAsync(db, "empty.txt", "   \n\t  ");
        var handler = new StubHandler(EmbedResponse);

        await CreateJob(db, handler).Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Failed, result.Status);
        Assert.Contains("No extractable text", result.Error);
        Assert.Equal(0, handler.RequestCount);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Execute_UnknownJob_ReturnsWithoutThrowing_AndCleansUpFile()
    {
        var db = CreateDb();
        var path = TempDirFile($"vox-test-{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(path, SampleText());
        var handler = new StubHandler(EmbedResponse);

        await CreateJob(db, handler).Execute(Guid.NewGuid(), path, CancellationToken.None);

        Assert.False(File.Exists(path));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Execute_PathOutsideStagingDir_IsIgnored_NeverDeletesOrProcesses()
    {
        var db = CreateDb();
        var jobId = Guid.NewGuid();
        db.TextbookJobs.Add(new TextbookJob { Id = jobId, FileName = "book.txt" });
        await db.SaveChangesAsync();

        var outside = Path.Combine(Path.GetTempPath(), $"vox-outside-{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(outside, SampleText());
        var handler = new StubHandler(EmbedResponse);

        try
        {
            await CreateJob(db, handler).Execute(jobId, outside, CancellationToken.None);

            Assert.True(File.Exists(outside), "file outside the staging dir must never be deleted");
            Assert.Equal(0, handler.RequestCount);
            var result = await db.TextbookJobs.SingleAsync(j => j.Id == jobId);
            Assert.Equal(TextbookJobStatus.Pending, result.Status);
            Assert.Empty(await db.TextbookChunks.ToListAsync());
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task Execute_RerunAfterPartialRun_PurgesStaleChunks_NoDuplicates()
    {
        var db = CreateDb();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText());
        // leftover half-embedded corpus from a crashed worker
        db.TextbookChunks.Add(new TextbookChunk
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            Content = "stale chunk from previous run",
            Source = "book.txt"
        });
        await db.SaveChangesAsync();

        var handler = new StubHandler(EmbedResponse);
        await CreateJob(db, handler).Execute(job.Id, path, CancellationToken.None);

        var chunks = await db.TextbookChunks.Where(c => c.JobId == job.Id).ToListAsync();
        Assert.Equal(2, chunks.Count);
        Assert.DoesNotContain(chunks, c => c.Content.StartsWith("stale"));
        var result = await db.TextbookJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Completed, result.Status);
        Assert.Equal(2, result.ProcessedChunks);
    }

    [Fact]
    public async Task Execute_SingleWindowInput_CompletesWithOneChunk()
    {
        var db = CreateDb();
        var shortText = string.Join(' ', Enumerable.Range(0, 100).Select(i => $"w{i}"));
        var (job, path) = await CreateJobRowWithFileAsync(db, "short.txt", shortText);
        var handler = new StubHandler(EmbedResponse);

        await CreateJob(db, handler).Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Completed, result.Status);
        Assert.Equal(1, result.TotalChunks);
        Assert.Equal(1, result.ProcessedChunks);
    }
}
