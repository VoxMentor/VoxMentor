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
/// Background tutor generation coverage (#72): Pending claim, Completed
/// persistence with tokens, Failed with stored error, and duplicate-skip.
/// </summary>
public class TutorSessionJobTests
{
    private sealed class FakeTutorService : ITutorService
    {
        public List<TutorChunk> Chunks { get; set; } = new() { new("Hel"), new("lo", true, 42) };
        public Exception? Throw { get; set; }

        public async IAsyncEnumerable<TutorChunk> StreamAnswerAsync(
            string question,
            Guid? conceptId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
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

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new TestCurrentUser());
    }

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

    private static TutorSessionJob CreateJob(ApplicationDbContext db, ITutorService tutor)
        => new(db, tutor, NullLogger<TutorSessionJob>.Instance);

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
        Assert.Null(session.Error);
    }

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
    }

    [Fact]
    public async Task Execute_MissingSession_IsNoOp()
    {
        var db = CreateDb();

        await CreateJob(db, new FakeTutorService()).Execute(Guid.NewGuid(), CancellationToken.None);
    }
}
