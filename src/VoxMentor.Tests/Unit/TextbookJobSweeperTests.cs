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
    public async Task Sweep_ReenqueuesOnlyStaleProcessingRow()
    {
        var db = CreateDb();
        var staleId = Guid.NewGuid();
        var freshId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        var staleFile = await StageFileAsync(staleId);
        var freshFile = await StageFileAsync(freshId);
        var pendingFile = await StageFileAsync(pendingId);
        try
        {
            db.TextbookJobs.AddRange(
                new TextbookJob
                {
                    Id = staleId,
                    FileName = "stale.txt",
                    Status = TextbookJobStatus.Processing,
                    RunId = Guid.NewGuid(),
                    ClaimedAt = DateTime.UtcNow - TimeSpan.FromHours(2)
                },
                new TextbookJob
                {
                    Id = freshId,
                    FileName = "fresh.txt",
                    Status = TextbookJobStatus.Processing,
                    RunId = Guid.NewGuid(),
                    ClaimedAt = DateTime.UtcNow
                },
                new TextbookJob
                {
                    Id = pendingId,
                    FileName = "pending.txt",
                    Status = TextbookJobStatus.Pending,
                    ClaimedAt = DateTime.UtcNow - TimeSpan.FromHours(2)
                });
            await db.SaveChangesAsync();

            var queue = new RecordingQueue();
            await new TextbookJobSweeper(db, queue, NullLogger<TextbookJobSweeper>.Instance)
                .SweepAsync(CancellationToken.None);

            var entry = Assert.Single(queue.Enqueued);
            Assert.Equal(staleId, entry.JobId);
            Assert.Equal(staleFile, entry.Path);
        }
        finally
        {
            File.Delete(staleFile);
            File.Delete(freshFile);
            File.Delete(pendingFile);
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
