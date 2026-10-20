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
                });
            await db.SaveChangesAsync();

            var queue = new RecordingQueue();
            await new TextbookJobSweeper(db, queue, NullLogger<TextbookJobSweeper>.Instance)
                .SweepAsync(CancellationToken.None);

            Assert.Equal(2, queue.Enqueued.Count);
            Assert.Contains(queue.Enqueued,
                e => e.JobId == staleProcessingId && e.Path == staleProcessingFile);
            Assert.Contains(queue.Enqueued,
                e => e.JobId == stalePendingId && e.Path == stalePendingFile);
        }
        finally
        {
            File.Delete(staleProcessingFile);
            File.Delete(freshProcessingFile);
            File.Delete(stalePendingFile);
            File.Delete(freshPendingFile);
        }
    }

    [Fact]
    public async Task Sweep_SkipsStaleRowWithoutStagedFile()
    {
        var db = CreateDb();
        db.TextbookJobs.Add(new TextbookJob
        {
            Id = Guid.NewGuid(),
            FileName = "gone.txt",
            Status = TextbookJobStatus.Processing,
            RunId = Guid.NewGuid(),
            ClaimedAt = DateTime.UtcNow - TimeSpan.FromHours(2)
        });
        await db.SaveChangesAsync();

        var queue = new RecordingQueue();
        await new TextbookJobSweeper(db, queue, NullLogger<TextbookJobSweeper>.Instance)
            .SweepAsync(CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }
}
