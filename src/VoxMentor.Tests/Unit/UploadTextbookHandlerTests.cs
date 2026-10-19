using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Admin.UploadTextbook;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Tests.Unit;

/// <summary>Upload handler coverage: job row, temp file, enqueue, concept validation (#70).</summary>
public class UploadTextbookHandlerTests
{
    private sealed class FakeQueue : ITextbookIngestionQueue
    {
        public Guid JobId { get; private set; }
        public string? FilePath { get; private set; }

        public void Enqueue(Guid jobId, string filePath)
        {
            JobId = jobId;
            FilePath = filePath;
        }
    }

    private sealed class ThrowingQueue : ITextbookIngestionQueue
    {
        public string? FilePath { get; private set; }

        public void Enqueue(Guid jobId, string filePath)
        {
            FilePath = filePath;
            throw new InvalidOperationException("queue down");
        }
    }

    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options, new TestCurrentUser());
    }

    private static UploadTextbookCommand Command(
        string fileName = "book.txt", long length = 100, Guid? conceptId = null)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("some textbook content");
        return new UploadTextbookCommand(fileName, new MemoryStream(bytes), length, conceptId);
    }

    [Fact]
    public async Task Upload_CreatesPendingJob_WritesTempFile_AndEnqueues()
    {
        var db = CreateDb();
        var queue = new FakeQueue();
        var handler = new UploadTextbookHandler(db, queue);

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.NotNull(result.Data);
        var job = await db.TextbookJobs.SingleAsync(j => j.Id == result.Data!.JobId);
        Assert.Equal(TextbookJobStatus.Pending, job.Status);
        Assert.Equal("book.txt", job.FileName);

        Assert.Equal(job.Id, queue.JobId);
        Assert.NotNull(queue.FilePath);
        Assert.True(File.Exists(queue.FilePath), "uploaded temp file should exist");

        File.Delete(queue.FilePath!);
    }

    [Fact]
    public async Task Upload_UnknownConcept_ThrowsNotFound_BeforeWritingFile()
    {
        var db = CreateDb();
        var queue = new FakeQueue();
        var handler = new UploadTextbookHandler(db, queue);

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(Command(conceptId: Guid.NewGuid()), CancellationToken.None));

        Assert.Null(queue.FilePath);
        Assert.Empty(await db.TextbookJobs.ToListAsync());
    }

    [Fact]
    public async Task Upload_KnownConcept_StoresConceptIdOnJob()
    {
        var db = CreateDb();
        var conceptId = Guid.NewGuid();
        db.Concepts.Add(new Concept { Id = conceptId, Name = "Arrays", Category = "DSA" });
        await db.SaveChangesAsync();

        var queue = new FakeQueue();
        var handler = new UploadTextbookHandler(db, queue);

        var result = await handler.Handle(Command(conceptId: conceptId), CancellationToken.None);

        var job = await db.TextbookJobs.SingleAsync(j => j.Id == result.Data!.JobId);
        Assert.Equal(conceptId, job.ConceptId);

        File.Delete(queue.FilePath!);
    }

    [Fact]
    public async Task Upload_ReturnsPollableMessage()
    {
        var db = CreateDb();
        var queue = new FakeQueue();
        var handler = new UploadTextbookHandler(db, queue);

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.Contains("Background job started", result.Data!.Message);
        Assert.Contains(result.Data.JobId.ToString(), result.Data.Message);
        Assert.Contains("textbook/status", result.Data.Message);

        File.Delete(queue.FilePath!);
    }

    [Fact]
    public async Task Upload_PostWriteFailure_DeletesStagedTempFile_AndRethrows()
    {
        var db = CreateDb();
        var queue = new ThrowingQueue();
        var handler = new UploadTextbookHandler(db, queue);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(Command(), CancellationToken.None));

        // Per-request assertion (not a directory count): other test classes
        // create/delete files in the same shared staging dir in parallel.
        Assert.NotNull(queue.FilePath);
        Assert.False(File.Exists(queue.FilePath), "staged file must be deleted when enqueue fails");
        Assert.Empty(await db.TextbookJobs.ToListAsync());
    }

    [Fact]
    public async Task Upload_HangfireDisabledQueue_Throws_LeavesNoPendingRow()
    {
        var db = CreateDb();
        var handler = new UploadTextbookHandler(db, new Infrastructure.Services.NullTextbookIngestionQueue());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(Command(), CancellationToken.None));

        Assert.Empty(await db.TextbookJobs.ToListAsync());
    }

    // Idempotent retries (#104 CodeRabbit): same content + same concept must
    // reuse the existing job instead of queueing a duplicate.

    [Fact]
    public async Task Upload_SameContentRetried_ReusesPendingJob_NoNewRow_NoEnqueue()
    {
        var db = CreateDb();
        var queue = new FakeQueue();
        var handler = new UploadTextbookHandler(db, queue);

        var first = await handler.Handle(Command(), CancellationToken.None);
        var second = await handler.Handle(Command(), CancellationToken.None);

        Assert.Equal(first.Data!.JobId, second.Data!.JobId);
        Assert.Contains("already running", second.Data!.Message);
        Assert.Single(await db.TextbookJobs.ToListAsync());
        Assert.Equal(first.Data.JobId, queue.JobId); // enqueued exactly once

        File.Delete(queue.FilePath!);
    }

    [Fact]
    public async Task Upload_SameContentAlreadyCompleted_ReturnsCompletedJobId()
    {
        var db = CreateDb();
        var queue = new FakeQueue();
        var handler = new UploadTextbookHandler(db, queue);

        var first = await handler.Handle(Command(), CancellationToken.None);
        var job = await db.TextbookJobs.SingleAsync(j => j.Id == first.Data!.JobId);
        job.Status = TextbookJobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var second = await handler.Handle(Command(), CancellationToken.None);

        Assert.Equal(first.Data!.JobId, second.Data!.JobId);
        Assert.Contains("already ingested", second.Data!.Message);
        Assert.Single(await db.TextbookJobs.ToListAsync());

        File.Delete(queue.FilePath!);
    }

    [Fact]
    public async Task Upload_SameContentFailedJob_CreatesFreshJob()
    {
        var db = CreateDb();
        var queue = new FakeQueue();
        var handler = new UploadTextbookHandler(db, queue);

        var first = await handler.Handle(Command(), CancellationToken.None);
        var firstPath = queue.FilePath!;
        var job = await db.TextbookJobs.SingleAsync(j => j.Id == first.Data!.JobId);
        job.Status = TextbookJobStatus.Failed;
        job.Error = "Ollama down";
        await db.SaveChangesAsync();

        var second = await handler.Handle(Command(), CancellationToken.None);

        Assert.NotEqual(first.Data!.JobId, second.Data!.JobId);
        Assert.Equal(2, (await db.TextbookJobs.ToListAsync()).Count);
        Assert.Equal(second.Data!.JobId, queue.JobId); // freshest run is the queued one
        Assert.Contains("Background job started", second.Data.Message);

        File.Delete(firstPath);
        File.Delete(queue.FilePath!);
    }

    [Fact]
    public async Task Upload_SameContentDifferentConcept_CreatesFreshJob()
    {
        var db = CreateDb();
        var conceptA = Guid.NewGuid();
        var conceptB = Guid.NewGuid();
        db.Concepts.Add(new Concept { Id = conceptA, Name = "Arrays", Category = "DSA" });
        db.Concepts.Add(new Concept { Id = conceptB, Name = "Graphs", Category = "DSA" });
        await db.SaveChangesAsync();

        var queue = new FakeQueue();
        var handler = new UploadTextbookHandler(db, queue);

        var first = await handler.Handle(Command(conceptId: conceptA), CancellationToken.None);
        var firstPath = queue.FilePath!;

        var second = await handler.Handle(Command(conceptId: conceptB), CancellationToken.None);

        Assert.NotEqual(first.Data!.JobId, second.Data!.JobId);
        Assert.Equal(2, (await db.TextbookJobs.ToListAsync()).Count);
        Assert.Equal(conceptB, await db.TextbookJobs
            .Where(j => j.Id == second.Data!.JobId)
            .Select(j => j.ConceptId)
            .SingleAsync());

        File.Delete(firstPath);
        File.Delete(queue.FilePath!);
    }

    [Fact]
    public async Task Upload_StoresContentHashOnJob()
    {
        var db = CreateDb();
        var queue = new FakeQueue();
        var handler = new UploadTextbookHandler(db, queue);

        var result = await handler.Handle(Command(), CancellationToken.None);

        var job = await db.TextbookJobs.SingleAsync(j => j.Id == result.Data!.JobId);
        Assert.Equal(64, job.ContentHash.Length);
        Assert.Matches("^[0-9A-F]{64}$", job.ContentHash);

        File.Delete(queue.FilePath!);
    }

    // Two-admin race (#104 CodeRabbit): the unique (ContentHash, ConceptId)
    // index rejects the colliding insert; the handler must return the winner.

    /// <summary>
    /// InMemory can't enforce unique indexes, so the race is simulated in the
    /// one window where a concurrent upload could commit: from inside the
    /// handler's own SaveChanges (after its lookup ran, before its insert
    /// lands). The winner is committed through the same InMemory store, then
    /// the unique-index failure its presence causes is thrown.
    /// </summary>
    private sealed class RaceWinnerDbContext : Infrastructure.Persistence.ApplicationDbContext
    {
        private readonly DbContextOptions<Infrastructure.Persistence.ApplicationDbContext> _options;
        private readonly ICurrentUserService _user;

        public TextbookJob Winner { get; init; } = null!;
        public int SaveAttempts { get; private set; }

        public RaceWinnerDbContext(
            DbContextOptions<Infrastructure.Persistence.ApplicationDbContext> options,
            ICurrentUserService user)
            : base(options, user)
        {
            _options = options;
            _user = user;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (++SaveAttempts == 1)
            {
                await using var winnerDb = new Infrastructure.Persistence.ApplicationDbContext(_options, _user);
                winnerDb.TextbookJobs.Add(Winner);
                await winnerDb.SaveChangesAsync(cancellationToken);
                throw new DbUpdateException("unique index collision (simulated)");
            }
            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Upload_DuplicateInsertLosesRace_ReturnsWinnerId_AndSkipsEnqueue()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var winnerHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("some textbook content")));
        var winnerId = Guid.NewGuid();
        var db = new RaceWinnerDbContext(options, new TestCurrentUser())
        {
            Winner = new TextbookJob
            {
                Id = winnerId,
                FileName = "book.txt",
                ContentHash = winnerHash,
                Status = TextbookJobStatus.Pending
            }
        };
        var queue = new FakeQueue();
        var handler = new UploadTextbookHandler(db, queue);

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.Equal(winnerId, result.Data!.JobId);
        Assert.Contains("already running", result.Data!.Message);
        Assert.Equal(Guid.Empty, queue.JobId); // the losing duplicate is never queued
        Assert.Single(await db.TextbookJobs.ToListAsync()); // only the winner persisted
        // The handler deleted its own staged duplicate in ReuseResponse.
    }
}
