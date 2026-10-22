using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Jobs;
using VoxMentor.Infrastructure.Persistence;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Reconciliation sweep coverage (#72): stale Streaming sessions fail by
/// stream-start age, stale Pending sessions re-queue behind a QueuedAt claim
/// (no per-sweep duplicates), fresh rows untouched.
/// </summary>
public class TutorSessionReconciliationJobTests
{
    /// <summary>Queue stub that records enqueued session ids.</summary>
    private sealed class FakeQueue : ITutorSessionQueue
    {
        /// <summary>Session ids passed to Enqueue.</summary>
        public List<Guid> Enqueued { get; } = new();
        /// <summary>Records the session id for later assertion.</summary>
        public void Enqueue(Guid sessionId) => Enqueued.Add(sessionId);
    }

    /// <summary>Creates an in-memory ApplicationDbContext with the fixed test user.</summary>
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new TestCurrentUser());
    }

    /// <summary>Seeds a TutorSession with the given status and timestamps, detached from the change tracker.</summary>
    private static TutorSession Seed(
        ApplicationDbContext db,
        TutorSessionStatus status,
        DateTime createdAt,
        DateTime? startedAt = null,
        DateTime? queuedAt = null)
    {
        var session = new TutorSession
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            Question = "q",
            Status = status,
            CreatedAt = createdAt,
            StartedAt = startedAt,
            QueuedAt = queuedAt
        };
        db.TutorSessions.Add(session);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return session;
    }

    /// <summary>Runs one reconciliation sweep with a null logger.</summary>
    private static Task Execute(ApplicationDbContext db, FakeQueue queue)
        => new TutorSessionReconciliationJob(db, queue, NullLogger<TutorSessionReconciliationJob>.Instance)
            .ExecuteAsync(CancellationToken.None);

    /// <summary>A Streaming session older than the timeout is marked Failed; a fresh Streaming row is untouched.</summary>
    [Fact]
    public async Task Execute_StuckStreaming_MarksFailedWithTimeoutError()
    {
        var db = CreateDb();
        var stale = Seed(db, TutorSessionStatus.Streaming, DateTime.UtcNow.AddMinutes(-11));
        var fresh = Seed(db, TutorSessionStatus.Streaming, DateTime.UtcNow.AddMinutes(-1));

        await Execute(db, new FakeQueue());

        var staleRow = await db.TutorSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == stale.Id);
        Assert.Equal(TutorSessionStatus.Failed, staleRow.Status);
        Assert.Equal("Generation timed out.", staleRow.Error);
        Assert.NotNull(staleRow.CompletedAt);

        var freshRow = await db.TutorSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == fresh.Id);
        Assert.Equal(TutorSessionStatus.Streaming, freshRow.Status);
        Assert.Null(freshRow.Error);
    }

    /// <summary>Streaming age is measured from StartedAt, not CreatedAt, so a just-started stream survives.</summary>
    [Fact]
    public async Task Execute_StreamingAgedFromStartedAtNotCreatedAt()
    {
        // A session can sit Pending in the queue for minutes before its stream
        // starts: age must come from StartedAt so an active stream isn't failed.
        var db = CreateDb();
        var queuedSinceCreation = Seed(
            db, TutorSessionStatus.Streaming,
            DateTime.UtcNow.AddMinutes(-11), startedAt: DateTime.UtcNow.AddMinutes(-11));
        var justStarted = Seed(
            db, TutorSessionStatus.Streaming,
            DateTime.UtcNow.AddMinutes(-11), startedAt: DateTime.UtcNow.AddSeconds(-30));

        await Execute(db, new FakeQueue());

        var staleRow = await db.TutorSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == queuedSinceCreation.Id);
        Assert.Equal(TutorSessionStatus.Failed, staleRow.Status);

        var activeRow = await db.TutorSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == justStarted.Id);
        Assert.Equal(TutorSessionStatus.Streaming, activeRow.Status);
    }

    /// <summary>A lost Pending session is re-queued with a fresh claim; a fresh Pending row is untouched.</summary>
    [Fact]
    public async Task Execute_LostPending_Requeues_FreshPendingUntouched()
    {
        var db = CreateDb();
        var lost = Seed(db, TutorSessionStatus.Pending, DateTime.UtcNow.AddMinutes(-3));
        var fresh = Seed(db, TutorSessionStatus.Pending, DateTime.UtcNow.AddSeconds(-10));
        var queue = new FakeQueue();

        await Execute(db, queue);

        Assert.Equal(new[] { lost.Id }, queue.Enqueued);
        var lostRow = await db.TutorSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == lost.Id);
        Assert.Equal(TutorSessionStatus.Pending, lostRow.Status);
        Assert.NotNull(lostRow.QueuedAt);
    }

    /// <summary>A Pending session enqueued recently is not re-enqueued by the sweep.</summary>
    [Fact]
    public async Task Execute_PendingEnqueuedRecently_NotReenqueued()
    {
        // The enqueue claim: a Pending session enqueued 30s ago is still
        // waiting on its worker — re-enqueueing it every sweep would pile up
        // duplicate jobs behind the generation lock.
        var db = CreateDb();
        var queued = Seed(
            db, TutorSessionStatus.Pending,
            DateTime.UtcNow.AddMinutes(-5), queuedAt: DateTime.UtcNow.AddSeconds(-30));
        var queue = new FakeQueue();

        await Execute(db, queue);

        Assert.Empty(queue.Enqueued);
        var row = await db.TutorSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == queued.Id);
        Assert.Equal(TutorSessionStatus.Pending, row.Status);
        // Unchanged claim: the sweep neither re-enqueued nor re-claimed it.
        Assert.True(row.QueuedAt < DateTime.UtcNow.AddSeconds(-20));
    }

    /// <summary>A Pending session whose enqueue claim is stale is re-enqueued once with a refreshed claim.</summary>
    [Fact]
    public async Task Execute_PendingEnqueueClaimStale_ReenqueuesOnce()
    {
        // Enqueued long ago but never picked up (lost job): re-queue it and
        // refresh the claim so the next sweep won't enqueue it again.
        var db = CreateDb();
        var lost = Seed(
            db, TutorSessionStatus.Pending,
            DateTime.UtcNow.AddMinutes(-5), queuedAt: DateTime.UtcNow.AddMinutes(-3));
        var queue = new FakeQueue();

        await Execute(db, queue);

        Assert.Equal(new[] { lost.Id }, queue.Enqueued);
        var row = await db.TutorSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == lost.Id);
        Assert.True(row.QueuedAt > DateTime.UtcNow.AddMinutes(-1));

        // Second sweep: claim is fresh → no duplicate enqueue.
        await Execute(db, queue);
        Assert.Equal(new[] { lost.Id }, queue.Enqueued);
    }

    /// <summary>With no stale rows the sweep enqueues nothing.</summary>
    [Fact]
    public async Task Execute_NoStaleRows_DoesNothing()
    {
        var db = CreateDb();
        Seed(db, TutorSessionStatus.Completed, DateTime.UtcNow.AddMinutes(-30));
        var queue = new FakeQueue();

        await Execute(db, queue);

        Assert.Empty(queue.Enqueued);
    }
}
