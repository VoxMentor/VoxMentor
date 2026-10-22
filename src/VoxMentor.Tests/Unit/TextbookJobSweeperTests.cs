using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Jobs;
using VoxMentor.Infrastructure.Persistence;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Recovery-sweep coverage (#104 CodeRabbit round 6): stale Processing rows
/// are re-enqueued from their staged file; fresh claims and non-Processing
/// rows are left alone.
/// </summary>
public class TextbookJobSweeperTests
{
    private sealed class RecordingQueue : ITextbookIngestionQueue
    {
        public List<(Guid JobId, string Path)> Enqueued { get; } = new();

        public void Enqueue(Guid jobId, string filePath) => Enqueued.Add((jobId, filePath));

        public void Schedule(Guid jobId, string filePath, TimeSpan delay) { }
    }

    private static ApplicationDbContext CreateDb()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new TestCurrentUser());

    private static async Task<string> StageFileAsync(Guid jobId)
    {
        var staging = Path.Combine(Path.GetTempPath(), ITextbookIngestionQueue.TempDirectoryName);
        Directory.CreateDirectory(staging);
        var path = Path.Combine(staging, $"{jobId}.txt");
        await File.WriteAllTextAsync(path, "staged");
        return path;
    }

    [Fact]
    public async Task Sweep_ReenqueuesOnlyStaleRows()
    {
        var db = CreateDb();
        var staleProcessingId = Guid.NewGuid();
        var freshProcessingId = Guid.NewGuid();
        var stalePendingId = Guid.NewGuid();
        var freshPendingId = Guid.NewGuid();
        var staleProcessingFile = await StageFileAsync(staleProcessingId);
        var freshProcessingFile = await StageFileAsync(freshProcessingId);
        var stalePendingFile = await StageFileAsync(stalePendingId);
        var freshPendingFile = await StageFileAsync(freshPendingId);
        // Legacy/crashed Processing row that never recorded a claim.
        var staleUnclaimedId = Guid.NewGuid();
        var staleUnclaimedFile = await StageFileAsync(staleUnclaimedId);
        try
        {
            var twoHoursAgo = DateTime.UtcNow - TimeSpan.FromHours(2);
            db.TextbookJobs.AddRange(
                new TextbookJob
                {
                    Id = staleProcessingId,
                    FileName = "stale-processing.txt",
                    Status = TextbookJobStatus.Processing,
                    RunId = Guid.NewGuid(),
                    ClaimedAt = twoHoursAgo,
                    CreatedAt = twoHoursAgo
                },
                new TextbookJob
                {
                    Id = freshProcessingId,
                    FileName = "fresh-processing.txt",
                    Status = TextbookJobStatus.Processing,
                    RunId = Guid.NewGuid(),
                    ClaimedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                },
                new TextbookJob
                {
                    Id = stalePendingId,
                    FileName = "stale-pending.txt",
                    Status = TextbookJobStatus.Pending,
                    CreatedAt = twoHoursAgo
                },
                new TextbookJob
                {
                    Id = freshPendingId,
                    FileName = "fresh-pending.txt",
                    Status = TextbookJobStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                },
                // Pre-RunId rows migrated from Legacy retain Processing with
                // no claim timestamp; the ClaimedAt == null branch must pick
                // them up or they stay wedged forever (#104 v3 TC-04).
                new TextbookJob
                {
                    Id = staleUnclaimedId,
                    FileName = "stale-unclaimed.txt",
                    Status = TextbookJobStatus.Processing,
                    ClaimedAt = null,
                    CreatedAt = twoHoursAgo
                });
            await db.SaveChangesAsync();

            var queue = new RecordingQueue();
            await new TextbookJobSweeper(db, queue, NullLogger<TextbookJobSweeper>.Instance)
                .SweepAsync(CancellationToken.None);

            Assert.Equal(3, queue.Enqueued.Count);
            Assert.Contains(queue.Enqueued,
                e => e.JobId == staleProcessingId && e.Path == staleProcessingFile);
            Assert.Contains(queue.Enqueued,
                e => e.JobId == stalePendingId && e.Path == stalePendingFile);
            Assert.Contains(queue.Enqueued,
                e => e.JobId == staleUnclaimedId && e.Path == staleUnclaimedFile);
        }
        finally
        {
            File.Delete(staleProcessingFile);
            File.Delete(freshProcessingFile);
            File.Delete(stalePendingFile);
            File.Delete(freshPendingFile);
            File.Delete(staleUnclaimedFile);
        }
    }

    // A stale row whose staged file is gone cannot ever run again — and dedup
    // hands the dead row back on every re-upload. The sweep must end it as
    // Failed so a re-upload starts fresh (#104).
    [Fact]
    public async Task Sweep_MarksFilelessStaleRowFailed()
    {
        var db = CreateDb();
        var filelessId = Guid.NewGuid();
        db.TextbookJobs.Add(new TextbookJob
        {
            Id = filelessId,
            FileName = "lost-after-restart.pdf",
            Status = TextbookJobStatus.Processing,
            RunId = Guid.NewGuid(),
            ClaimedAt = DateTime.UtcNow - TimeSpan.FromHours(2)
        });
        // A partially ingested run can leave chunks behind; TextbookChunks
        // has no FK back to the job, so the Failed flip must purge them.
        db.TextbookChunks.Add(new TextbookChunk
        {
            Id = Guid.NewGuid(),
            JobId = filelessId,
            Content = "orphan chunk from a dead run",
            Source = "lost-after-restart.pdf"
        });
        // A second, live job: the purge must not touch its corpus.
        var otherJobId = Guid.NewGuid();
        db.TextbookJobs.Add(new TextbookJob
        {
            Id = otherJobId,
            FileName = "still-live.pdf",
            Status = TextbookJobStatus.Processing,
            RunId = Guid.NewGuid(),
            ClaimedAt = DateTime.UtcNow
        });
        db.TextbookChunks.Add(new TextbookChunk
        {
            Id = Guid.NewGuid(),
            JobId = otherJobId,
            Content = "live chunk",
            Source = "still-live.pdf"
        });
        await db.SaveChangesAsync();

        var queue = new RecordingQueue();
        await new TextbookJobSweeper(db, queue, NullLogger<TextbookJobSweeper>.Instance)
            .SweepAsync(CancellationToken.None);

        // The sweep shares this context's tracked state, so re-read without
        // tracking from the store side.
        var row = await db.TextbookJobs.AsNoTracking().SingleAsync(j => j.Id == filelessId);
        Assert.Equal(TextbookJobStatus.Failed, row.Status);
        Assert.NotNull(row.Error);
        Assert.Contains("Re-upload", row.Error!);
        Assert.NotNull(row.CompletedAt);
        Assert.False(
            await db.TextbookChunks.AnyAsync(c => c.JobId == filelessId),
            "the Failed flip purges the orphaned partial corpus");
        Assert.Null(row.RunId); // terminal-token rotation fences the dead run's next save
        Assert.True(
            await db.TextbookChunks.AnyAsync(c => c.JobId == otherJobId),
            "the purge is scoped to the fileless job, not the whole table");
        Assert.Empty(queue.Enqueued);
    }

    // An already-Failed fileless row must not be re-touched: the sweeper's
    // update predicate scopes to non-Failed rows, so a second sweep is a no-op
    // and the re-marker's explanatory Error stays intact.
    [Fact]
    public async Task Sweep_AlreadyFailedFilelessRow_LeftUntouched()
    {
        var db = CreateDb();
        var alreadyFailedId = Guid.NewGuid();
        const string originalError = "Original failure from the re-marker.";
        db.TextbookJobs.Add(new TextbookJob
        {
            Id = alreadyFailedId,
            FileName = "failed.txt",
            Status = TextbookJobStatus.Failed,
            Error = originalError,
            CreatedAt = DateTime.UtcNow - TimeSpan.FromHours(2)
        });
        await db.SaveChangesAsync();

        var queue = new RecordingQueue();
        await new TextbookJobSweeper(db, queue, NullLogger<TextbookJobSweeper>.Instance)
            .SweepAsync(CancellationToken.None);

        var row = await db.TextbookJobs.SingleAsync(j => j.Id == alreadyFailedId);
        Assert.Equal(TextbookJobStatus.Failed, row.Status);
        Assert.Equal(originalError, row.Error);
        Assert.Null(row.CompletedAt);
        Assert.Empty(queue.Enqueued);
    }
}
