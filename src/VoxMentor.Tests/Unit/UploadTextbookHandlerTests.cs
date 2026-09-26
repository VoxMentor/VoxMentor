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
        return new Infrastructure.Persistence.ApplicationDbContext(options);
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
    }
}
