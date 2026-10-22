using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Background tutor generation coverage (#72): Pending claim (with its
/// concurrency-token conflict), Completed persistence with tokens, Failed
/// with stored error, final-chunk gating, and duplicate-skip.
/// </summary>
public class TutorSessionJobTests
{
    /// <summary>Tutor service stub that yields preset chunks, optionally throwing mid-stream.</summary>
    private sealed class FakeTutorService : ITutorService
    {
        /// <summary>Chunks to yield from the fake stream.</summary>
        public List<TutorChunk> Chunks { get; set; } = new() { new("Hel"), new("lo", true, 42) };
        /// <summary>Exception to throw when set, before yielding any chunk.</summary>
        public Exception? Throw { get; set; }
        /// <summary>Callback invoked as the stream starts, for mid-stream side effects.</summary>
        public Action? OnStream { get; set; }
        /// <summary>Number of StreamAnswerAsync invocations.</summary>
        public int Calls { get; private set; }

        /// <summary>Fake answer stream: increments Calls, fires OnStream, optionally throws, then yields Chunks.</summary>
        public async IAsyncEnumerable<TutorChunk> StreamAnswerAsync(
            string question,
            Guid? conceptId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            OnStream?.Invoke();
            if (Throw is not null)
            {
                throw Throw;
            }
            foreach (var chunk in Chunks)
            {
                yield return chunk;
            }
        }
    }

    /// <summary>Creates an in-memory ApplicationDbContext; pass storeName to share a database across contexts.</summary>
    private static ApplicationDbContext CreateDb(string? storeName = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(storeName ?? Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new TestCurrentUser());
    }

    /// <summary>Seeds a TutorSession with the given status and detaches it from the change tracker.</summary>
    private static TutorSession SeedPending(ApplicationDbContext db, TutorSessionStatus status = TutorSessionStatus.Pending)
    {
        var session = new TutorSession
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            Question = "Why is Kadane O(n)?",
            Status = status
        };
        db.TutorSessions.Add(session);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return session;
    }

    /// <summary>Builds a TutorSessionJob with a null logger.</summary>
    private static TutorSessionJob CreateJob(ApplicationDbContext db, ITutorService tutor)
        => new(db, tutor, NullLogger<TutorSessionJob>.Instance);

    /// <summary>Pending session completes with concatenated answer, token count, and timestamps filled.</summary>
    [Fact]
    public async Task Execute_PendingSession_PersistsCompletedAnswerAndTokens()
    {
        var db = CreateDb();
        var seeded = SeedPending(db);

        await CreateJob(db, new FakeTutorService()).Execute(seeded.Id, CancellationToken.None);

        var session = await db.TutorSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == seeded.Id);
        Assert.Equal(TutorSessionStatus.Completed, session.Status);
        Assert.Equal("Hello", session.Answer);
        Assert.Equal(42, session.TotalTokens);
        Assert.NotNull(session.CompletedAt);
        Assert.NotNull(session.StartedAt);
        Assert.Null(session.Error);
    }

    /// <summary>A tutor service exception marks the session Failed and stores the error message.</summary>
    [Fact]
    public async Task Execute_TutorThrows_MarksFailedStoresError()
    {
        var db = CreateDb();
        var seeded = SeedPending(db);

        await CreateJob(db, new FakeTutorService
        {
            Throw = new InvalidOperationException("Tutor service temporarily unavailable.")
        }).Execute(seeded.Id, CancellationToken.None);

        var session = await db.TutorSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == seeded.Id);
        Assert.Equal(TutorSessionStatus.Failed, session.Status);
        Assert.Equal("Tutor service temporarily unavailable.", session.Error);
        Assert.NotNull(session.CompletedAt);
    }

    /// <summary>A stream that ends without a final chunk fails the session and does not persist the partial answer.</summary>
    [Fact]
    public async Task Execute_StreamEndsWithoutFinalChunk_MarksFailedNotCompleted()
    {
        var db = CreateDb();
        var seeded = SeedPending(db);
        // Ollama 200 + truncated body: content lines but no done=true event.
        var tutor = new FakeTutorService { Chunks = new List<TutorChunk> { new("partial answer") } };

        await CreateJob(db, tutor).Execute(seeded.Id, CancellationToken.None);

        var session = await db.TutorSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == seeded.Id);
        Assert.Equal(TutorSessionStatus.Failed, session.Status);
        Assert.Contains("final stream chunk", session.Error);
        // MarkFailed always stores an answer value; truncated content must not
        // be persisted as if it were the completed answer.
        Assert.Equal(string.Empty, session.Answer);
        Assert.NotNull(session.CompletedAt);
    }

    /// <summary>Claim concurrency-token conflict (another worker claimed first) skips generation without calling the tutor.</summary>
    [Fact]
    public async Task Execute_AnotherWorkerClaimedFirst_ClaimConflictSkipsGeneration()
    {
        var store = Guid.NewGuid().ToString();
        var db = CreateDb(store);
        var seeded = SeedPending(db);
        // Load Pending into the job's context so its claim write is based on a
        // stale original — the other worker wins the race between load and save.
        _ = db.TutorSessions.IgnoreQueryFilters().Single(s => s.Id == seeded.Id);

        var otherWorker = CreateDb(store);
        var claimed = await otherWorker.TutorSessions.IgnoreQueryFilters()
            .SingleAsync(s => s.Id == seeded.Id);
        claimed.Status = TutorSessionStatus.Streaming;
        claimed.StartedAt = DateTime.UtcNow;
        await otherWorker.SaveChangesAsync();

        var tutor = new FakeTutorService();
        await CreateJob(db, tutor).Execute(seeded.Id, CancellationToken.None);

        Assert.Equal(0, tutor.Calls);
        var session = await db.TutorSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == seeded.Id);
        Assert.Equal(TutorSessionStatus.Streaming, session.Status);
        Assert.Null(session.Answer);
    }

    /// <summary>The reconciliation sweep failing a session mid-stream is not overwritten by a later completion.</summary>
    [Fact]
    public async Task Execute_ReconciliationFailedMidStream_CompletionDoesNotOverwrite()
    {
        var store = Guid.NewGuid().ToString();
        var db = CreateDb(store);
        var seeded = SeedPending(db);
        var otherWorker = CreateDb(store);

        var tutor = new FakeTutorService
        {
            // The sweep fails the session while generation is streaming.
            OnStream = () =>
            {
                var row = otherWorker.TutorSessions.IgnoreQueryFilters()
                    .Single(s => s.Id == seeded.Id);
                row.Status = TutorSessionStatus.Failed;
                row.Error = "Generation timed out.";
                row.CompletedAt = DateTime.UtcNow;
                otherWorker.SaveChanges();
            }
        };

        await CreateJob(db, tutor).Execute(seeded.Id, CancellationToken.None);

        var session = await db.TutorSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == seeded.Id);
        Assert.Equal(TutorSessionStatus.Failed, session.Status);
        Assert.Equal("Generation timed out.", session.Error);
        Assert.Null(session.Answer);
    }

    /// <summary>An already-processed (non-pending) session is skipped without calling the tutor service.</summary>
    [Fact]
    public async Task Execute_AlreadyProcessed_Skips()
    {
        var db = CreateDb();
        var seeded = SeedPending(db, TutorSessionStatus.Completed);
        var tutor = new FakeTutorService();

        await CreateJob(db, tutor).Execute(seeded.Id, CancellationToken.None);

        var session = await db.TutorSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == seeded.Id);
        Assert.Null(session.Answer);
        Assert.Equal(TutorSessionStatus.Completed, session.Status);
        Assert.Equal(0, tutor.Calls);
    }

    /// <summary>A missing session id is a silent no-op.</summary>
    [Fact]
    public async Task Execute_MissingSession_IsNoOp()
    {
        var db = CreateDb();

        await CreateJob(db, new FakeTutorService()).Execute(Guid.NewGuid(), CancellationToken.None);
    }
}
