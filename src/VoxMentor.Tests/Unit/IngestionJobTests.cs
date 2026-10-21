using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector;
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
        return new ApplicationDbContext(options, new TestCurrentUser());
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

    private static IngestionJob CreateJob(ApplicationDbContext db, StubHandler handler, ITextbookIngestionQueue? queue = null)
        => new(db, CreateEmbedder(handler), queue ?? new FakeQueue(), NullLogger<IngestionJob>.Instance);

    /// <summary>Records Schedule calls (stale-claim recovery) instead of dialing Hangfire.</summary>
    private sealed class FakeQueue : ITextbookIngestionQueue
    {
        public List<(Guid JobId, string Path, TimeSpan Delay)> Scheduled { get; } = new();

        public void Enqueue(Guid jobId, string filePath) { }

        public void Schedule(Guid jobId, string filePath, TimeSpan delay)
            => Scheduled.Add((jobId, filePath, delay));
    }

    /// <summary>Schedule always throws — recovery scheduling must be best-effort.</summary>
    private sealed class ThrowingScheduleQueue : ITextbookIngestionQueue
    {
        public void Enqueue(Guid jobId, string filePath) { }

        public void Schedule(Guid jobId, string filePath, TimeSpan delay)
            => throw new InvalidOperationException("hangfire storage down (simulated)");
    }

    /// <summary>Throws DbUpdateConcurrencyException on the flagged save — simulates
    /// losing the RunId claim race on the store (#104).</summary>
    private sealed class ClaimRaceDbContext : ApplicationDbContext
    {
        public bool FailNextSave { get; init; }

        public ClaimRaceDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options, new TestCurrentUser()) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                throw new DbUpdateConcurrencyException("claim lost (simulated)");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

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
        var queue = new FakeQueue();

        await CreateJob(db, handler, queue)
            .Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Completed, result.Status);
        Assert.Equal(2, result.TotalChunks);
        Assert.Equal(2, result.ProcessedChunks);
        Assert.Null(result.Error);
        Assert.NotNull(result.CompletedAt);
        // The Completed publish rotates the ownership token (#104 v3 CS-5):
        // an earlier-loaded recovery's claim save must no longer match.
        Assert.Null(result.RunId);
        // Per-chunk ClaimedAt heartbeat stays set through the run (#104
        // review gate INGEST-002): recoveries treat the row as abandoned only
        // after a full threshold without a save.
        Assert.NotNull(result.ClaimedAt);

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

        // The claim scheduled its expiry watchdog (#104 review gate): a hung
        // owner keeps Hangfire's heartbeat, so without this no replay would
        // ever exist to schedule recovery.
        var watchdog = Assert.Single(queue.Scheduled);
        Assert.Equal(job.Id, watchdog.JobId);
        Assert.Equal(path, watchdog.Path);
        Assert.Equal(TimeSpan.FromHours(1), watchdog.Delay);

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
        Assert.Null(result.RunId); // the terminal Failed write rotates the token

        Assert.Empty(await db.TextbookChunks.ToListAsync());
        Assert.False(File.Exists(path), "temp file should be deleted even on failure");
    }

    [Fact]
    public async Task Execute_ProcessingRow_RerunsAndPurgesStaleChunks()
    {
        var db = CreateDb();
        var conceptId = Guid.NewGuid();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText(), conceptId);
        var staleChunk = new TextbookChunk
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            ConceptId = conceptId,
            Content = "stale",
            Source = "book.txt",
            Embedding = new Vector(new float[768]),
        };
        db.TextbookChunks.Add(staleChunk);
        job.Status = TextbookJobStatus.Processing;
        job.ProcessedChunks = 3;
        await db.SaveChangesAsync();
        var handler = new StubHandler(EmbedResponse);

        await CreateJob(db, handler).Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Completed, result.Status);
        Assert.Equal(2, result.TotalChunks);
        Assert.Equal(2, result.ProcessedChunks);
        Assert.NotNull(result.CompletedAt);
        var chunks = await db.TextbookChunks.Where(c => c.JobId == job.Id).ToListAsync();
        Assert.Equal(2, chunks.Count);
        Assert.DoesNotContain(chunks, c => c.Content == "stale");
        Assert.Equal(2, handler.RequestCount);
        Assert.False(File.Exists(path), "temp file should be deleted after processing");
    }

    [Fact]
    public async Task Execute_FailedMidRun_AbortsAndPurgesChunkResidue()
    {
        var db = CreateDb();
        var conceptId = Guid.NewGuid();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText(), conceptId);
        var calls = 0;
        var handler = new StubHandler(() =>
        {
            // Simulate the migration reconciling this row between the
            // worker's per-chunk saves: flip it Failed mid-run.
            if (++calls == 2)
            {
                job.Status = TextbookJobStatus.Failed;
                db.SaveChanges();
            }
            return EmbedResponse();
        });

        await CreateJob(db, handler).Execute(job.Id, path, CancellationToken.None);

        // Store read (not the tracked instance) proves the worker did not
        // resurrect Completed over the reconciled Failed row.
        var result = await db.TextbookJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Failed, result.Status);
        Assert.Equal(2, handler.RequestCount);
        Assert.False(await db.TextbookChunks.AnyAsync(c => c.JobId == job.Id),
            "the abort must purge chunks the worker re-saved after the reconciliation delete");
        Assert.False(File.Exists(path), "temp file should be deleted after processing");
    }

    [Fact]
    public async Task Execute_CompletedRow_ZombieAbort_PurgesOnlyOwnChunks()
    {
        // A duplicate worker flips the row Completed mid-run (winner). This
        // worker must drop only the chunks it persisted; the winner's corpus
        // stays intact because removal is keyed by this run's chunk Ids, not
        // by JobId wholesale. In this single-context simulation every
        // persisted chunk is this run's, so the store ends up empty; a real
        // winner's chunks carry different Ids and survive the Id-based removal.
        var db = CreateDb();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText());
        var calls = 0;
        var handler = new StubHandler(() =>
        {
            if (++calls == 2)
            {
                job.Status = TextbookJobStatus.Completed;
                job.CompletedAt = DateTime.UtcNow;
                db.SaveChanges();
            }
            return EmbedResponse();
        });

        await CreateJob(db, handler).Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Completed, result.Status);
        Assert.Equal(2, handler.RequestCount);
        Assert.Empty(await db.TextbookChunks.ToListAsync());
        Assert.False(File.Exists(path), "temp file should be deleted after processing");
    }

    [Fact]
    public async Task Execute_AlreadyClaimedByAnotherRun_BacksOffWithoutTouchingCorpus()
    {
        // Run B finds the row already claimed by run A (Processing + RunId
        // set): it must not re-claim, purge, embed, or publish — the winner's
        // corpus stays intact and the staged file survives for the scheduled
        // recovery run.
        var db = CreateDb();
        var conceptId = Guid.NewGuid();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText(), conceptId);
        var otherRunId = Guid.NewGuid();
        job.Status = TextbookJobStatus.Processing;
        job.RunId = otherRunId;
        job.ClaimedAt = DateTime.UtcNow; // active claim: the other run is still alive
        db.TextbookChunks.Add(new TextbookChunk
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            ConceptId = conceptId,
            Content = "winner chunk",
            Source = "book.txt"
        });
        await db.SaveChangesAsync();
        var handler = new StubHandler(EmbedResponse);
        var queue = new FakeQueue();

        await CreateJob(db, handler, queue).Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Processing, result.Status);
        Assert.Equal(otherRunId, result.RunId);
        Assert.Equal(0, handler.RequestCount);
        var chunks = await db.TextbookChunks.Where(c => c.JobId == job.Id).ToListAsync();
        Assert.Single(chunks);
        Assert.Equal("winner chunk", chunks[0].Content);
        // A losing run must NOT delete the staged file: replayed Hangfire
        // executions share the same filePath, and the scheduled recovery
        // needs it (#104 CodeRabbit round 5).
        Assert.True(File.Exists(path), "a non-owner run must not delete the staged file");
        Assert.Equal(SampleText(), File.ReadAllText(path));
        var scheduled = Assert.Single(queue.Scheduled);
        Assert.Equal(job.Id, scheduled.JobId);
        Assert.Equal(path, scheduled.Path);
        Assert.True(scheduled.Delay > TimeSpan.Zero, "recovery scheduled after the claim expiry");
        Assert.True(
            scheduled.Delay <= TimeSpan.FromHours(1),
            "recovery scheduled within the staleness threshold");
        File.Delete(path);
    }

    [Fact]
    public async Task Execute_StaleClaimedRow_ReclaimsAndProcesses()
    {
        // A worker crashed after claiming: the row is Processing + RunId +
        // an old ClaimedAt. A new run reclaims it, purges the stale corpus,
        // and completes fresh.
        var db = CreateDb();
        var conceptId = Guid.NewGuid();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText(), conceptId);
        var deadRunId = Guid.NewGuid();
        var staleClaim = DateTime.UtcNow - TimeSpan.FromHours(2);
        job.Status = TextbookJobStatus.Processing;
        job.RunId = deadRunId;
        job.ClaimedAt = staleClaim;
        db.TextbookChunks.Add(new TextbookChunk
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            ConceptId = conceptId,
            Content = "stale chunk",
            Source = "book.txt"
        });
        await db.SaveChangesAsync();
        var handler = new StubHandler(EmbedResponse);

        await CreateJob(db, handler).Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Completed, result.Status);
        Assert.NotEqual(deadRunId, result.RunId);
        Assert.NotNull(result.ClaimedAt);
        Assert.True(result.ClaimedAt > staleClaim, "the reclaim refreshed ClaimedAt");
        Assert.Equal(2, handler.RequestCount);
        var chunks = await db.TextbookChunks.Where(c => c.JobId == job.Id).ToListAsync();
        Assert.Equal(2, chunks.Count);
        Assert.DoesNotContain(chunks, c => c.Content == "stale chunk");
        Assert.False(File.Exists(path), "temp file should be deleted after processing");
    }

    [Fact]
    public async Task Execute_ClaimRotatedMidRun_DoesNotPublish_KeepsNewOwner()
    {
        // Mid-run the fresh claim rotates out-of-band: another actor owns the
        // still-Processing row. The gate must compare the STORE read — this
        // run's tracked instance still carries its own RunId, so a tracked
        // comparison would publish Completed over the new owner — and this
        // run must drop only its own chunks and keep the staged file.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options, new TestCurrentUser());
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText());
        var newOwnerRunId = Guid.NewGuid();
        var calls = 0;
        var handler = new StubHandler(() =>
        {
            if (++calls == 2)
            {
                // A sibling context so this run's tracked copy stays stale —
                // the production shape of a write from another connection.
                using var newOwner = new ApplicationDbContext(options, new TestCurrentUser());
                var stored = newOwner.TextbookJobs.Single(j => j.Id == job.Id);
                stored.RunId = newOwnerRunId;
                stored.ClaimedAt = DateTime.UtcNow; // fresh claim by the new owner
                newOwner.TextbookChunks.Add(new TextbookChunk
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    Content = "winner chunk",
                    Source = "book.txt"
                });
                newOwner.SaveChanges();
            }
            return EmbedResponse();
        });

        await CreateJob(db, handler).Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Processing, result.Status);
        Assert.Equal(newOwnerRunId, result.RunId);
        Assert.Null(result.CompletedAt);
        Assert.Equal(2, handler.RequestCount);
        // Only this run's chunks are dropped: the new owner's chunk (written
        // through a real sibling context) survives the join-race purge
        // (#104 v3 TC-02).
        var survivor = Assert.Single(await db.TextbookChunks.AsNoTracking().ToListAsync());
        Assert.Equal("winner chunk", survivor.Content);
        Assert.True(
            File.Exists(path),
            "a live owner may still be mid-extraction; the staged file is kept");
        File.Delete(path);
    }

    [Fact]
    public async Task Execute_EmbedFailsAfterClaimRotation_KeepsFileAndOwnerRow()
    {
        // Embed failure while a foreign run owns the still-Processing row:
        // MarkFailedAsync must not fail that row or purge beyond this run's
        // chunks, and must keep the staged file — the owner may still be
        // mid-extraction (#104 round-5 keep-file contract).
        var db = CreateDb();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText());
        var newOwnerRunId = Guid.NewGuid();
        var calls = 0;
        var handler = new StubHandler(() =>
        {
            if (++calls == 2)
            {
                job.RunId = newOwnerRunId;
                job.ClaimedAt = DateTime.UtcNow; // fresh claim by the new owner
                db.SaveChanges();
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            return EmbedResponse();
        });

        await CreateJob(db, handler).Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Processing, result.Status);
        Assert.Equal(newOwnerRunId, result.RunId);
        Assert.Null(result.CompletedAt);
        Assert.Null(result.Error);
        Assert.Equal(2, handler.RequestCount);
        Assert.Empty(await db.TextbookChunks.ToListAsync());
        Assert.True(File.Exists(path), "a foreign owner may still need the staged file");
        File.Delete(path);
    }

    [Fact]
    public async Task Execute_ZombieThrowsAfterWinnerCompleted_PurgesOnlyOwnChunks()
    {
        // Winner published Completed mid-run; this zombie throws. The row
        // stays Completed and only this run's persisted chunks are dropped —
        // a real winner's chunks carry different Ids and survive. In this
        // single-context simulation every persisted chunk is this run's.
        var db = CreateDb();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText());
        var calls = 0;
        var handler = new StubHandler(() =>
        {
            if (++calls == 2)
            {
                job.Status = TextbookJobStatus.Completed;
                job.CompletedAt = DateTime.UtcNow;
                db.SaveChanges();
            }
            return calls == 2
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : EmbedResponse();
        });

        await CreateJob(db, handler).Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Completed, result.Status);
        Assert.Equal(2, handler.RequestCount);
        Assert.Empty(await db.TextbookChunks.ToListAsync());
        Assert.False(File.Exists(path), "temp file should be deleted after processing");
    }

    [Fact]
    public async Task Execute_FailedMidRun_SecondFailure_KeepsReconcilersMetadata()
    {
        // A re-mark keeps the row-ender's Error/CompletedAt: the reconciler
        // already explained why this run is over.
        var db = CreateDb();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText());
        var reconcilerError = "Job created before content-hash tracking. Re-upload the file to ingest it.";
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var handler = new StubHandler(() =>
        {
            if (++calls == 1)
            {
                // A separate connection in production: end the row between
                // the worker's saves, then kill the worker's embed call.
                job.Status = TextbookJobStatus.Failed;
                job.Error = reconcilerError;
                job.CompletedAt = DateTime.UtcNow;
                db.SaveChanges();
                cts.Cancel();
            }
            return EmbedResponse();
        });

        await CreateJob(db, handler).Execute(job.Id, path, cts.Token);

        var result = await db.TextbookJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Failed, result.Status);
        Assert.Contains("before content-hash tracking", result.Error);
        Assert.NotNull(result.CompletedAt);
        Assert.Equal(1, handler.RequestCount);
        Assert.Empty(await db.TextbookChunks.ToListAsync());
        Assert.False(File.Exists(path), "temp file should be deleted after processing");
    }

    [Fact]
    public async Task Execute_CompletedAtEntry_SkipsWork_KeepsCorpus()
    {
        // Operator requeue of a finished job must not resurrect work: the
        // stale-chunk purge would otherwise wipe the winner's corpus and the
        // extractor would then fail on the already-deleted temp file.
        var db = CreateDb();
        var conceptId = Guid.NewGuid();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText(), conceptId);
        db.TextbookChunks.Add(new TextbookChunk
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            ConceptId = conceptId,
            Content = "winner chunk",
            Source = "book.txt"
        });
        job.Status = TextbookJobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var handler = new StubHandler(EmbedResponse);

        await CreateJob(db, handler).Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Completed, result.Status);
        Assert.Equal(0, handler.RequestCount);
        Assert.Equal("winner chunk", (await db.TextbookChunks.SingleAsync(c => c.JobId == job.Id)).Content);
        Assert.False(File.Exists(path), "temp file should be deleted after processing");
    }

    [Fact]
    public async Task Execute_FailedJob_SkipsWork_LeavesRowFailed_AndCleansUpFile()
    {
        var db = CreateDb();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText());
        job.Status = TextbookJobStatus.Failed;
        job.Error = "Job created before content-hash tracking. Re-upload the file to ingest it.";
        await db.SaveChangesAsync();
        var handler = new StubHandler(EmbedResponse);

        await CreateJob(db, handler).Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Failed, result.Status);
        Assert.Contains("before content-hash tracking", result.Error);
        Assert.Equal(0, handler.RequestCount);
        Assert.Empty(await db.TextbookChunks.ToListAsync());
        Assert.False(File.Exists(path), "staged file of a failed job is still cleaned up");
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

    [Fact]
    public async Task Execute_ClaimLostConcurrency_SchedulesRecovery_KeepsFile_LeavesRowPending()
    {
        // Losing the RunId claim race throws DbUpdateConcurrencyException on
        // the claim save: the loser must return without failing the row
        // (failing it would fight the winner), keep the staged file for the
        // owner and the scheduled recovery, and hand in the watchdog (#104
        // review gate INGEST-001).
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var (job, path) = await CreateJobRowWithFileAsync(
            new ApplicationDbContext(options, new TestCurrentUser()), "book.txt", SampleText());
        var queue = new FakeQueue();

        await CreateJob(
                new ClaimRaceDbContext(options) { FailNextSave = true },
                new StubHandler(EmbedResponse), queue)
            .Execute(job.Id, path, CancellationToken.None);

        Assert.True(
            File.Exists(path),
            "a claim loser must not delete the staged file the recovery needs");
        var recovery = Assert.Single(queue.Scheduled);
        Assert.Equal(job.Id, recovery.JobId);
        Assert.Equal(path, recovery.Path);
        Assert.Equal(TimeSpan.FromHours(1), recovery.Delay);

        // The store row is untouched: the tracked claim changes were rejected
        // with the simulated conflict.
        using var store = new ApplicationDbContext(options, new TestCurrentUser());
        var row = await store.TextbookJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Pending, row.Status);
        Assert.Null(row.RunId);

        File.Delete(path); // the recovery run would reclaim it; the test only cleans up
    }

    [Fact]
    public async Task Execute_ScheduleUnavailable_RunStillReachesTerminalState()
    {
        // A Hangfire storage hiccup must not fault the run: recovery
        // scheduling is deliberately best-effort, and the 15-minute sweep is
        // the backstop for a broken recovery chain (#104 review gate
        // INGEST-003).
        var db = CreateDb();
        var (job, path) = await CreateJobRowWithFileAsync(db, "book.txt", SampleText());
        var handler = new StubHandler(EmbedResponse);

        await CreateJob(db, handler, new ThrowingScheduleQueue())
            .Execute(job.Id, path, CancellationToken.None);

        var result = await db.TextbookJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(TextbookJobStatus.Completed, result.Status);
        Assert.Equal(2, result.ProcessedChunks);
        Assert.False(File.Exists(path));
    }
}
