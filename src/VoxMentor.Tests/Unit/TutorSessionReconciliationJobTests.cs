using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Jobs;
using VoxMentor.Infrastructure.Persistence;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Reconciliation sweep coverage (#72): stale Streaming sessions fail, stale
/// Pending sessions re-queue, fresh rows untouched.
/// </summary>
public class TutorSessionReconciliationJobTests
{
    private sealed class FakeQueue : ITutorSessionQueue
    {
        public List<Guid> Enqueued { get; } = new();
        public void Enqueue(Guid sessionId) => Enqueued.Add(sessionId);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new TestCurrentUser());
    }

    private static TutorSession Seed(
        ApplicationDbContext db,
        TutorSessionStatus status,
        DateTime createdAt)
    {
        var session = new TutorSession
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            Question = "q",
            Status = status,
            CreatedAt = createdAt
        };
        db.TutorSessions.Add(session);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return session;
    }

    private static Task Execute(ApplicationDbContext db, FakeQueue queue)
        => new TutorSessionReconciliationJob(db, queue, NullLogger<TutorSessionReconciliationJob>.Instance)
            .ExecuteAsync(CancellationToken.None);

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
    }

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
