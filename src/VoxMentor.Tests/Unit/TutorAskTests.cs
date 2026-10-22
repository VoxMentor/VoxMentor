using FluentValidation;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Tutor.AskTutor;
using VoxMentor.Application.Features.Tutor.GetTutorSession;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.RateLimiting;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Coverage for tutor ask: rate limit window, session create/poll, ownership 404,
/// concept validation, and FluentValidation rules.
/// </summary>
public class TutorAskTests
{
    /// <summary>Fixed current-user stub.</summary>
    private sealed class FakeCurrentUser : ICurrentUserService
    {
        /// <summary>Configurable user id; null simulates an unauthenticated request.</summary>
        public string? UserId { get; set; } = "user-1";
    }

    /// <summary>Clock stub that can be advanced manually.</summary>
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        /// <summary>Advances the stub clock by the given span.</summary>
        public void Advance(TimeSpan by) => _now += by;
        /// <summary>Returns the current stubbed time.</summary>
        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>Fixed circuit stub; IsOpen is settable and record calls are no-ops.</summary>
    private sealed class FakeCircuit : ITutorCircuit
    {
        /// <summary>Whether the circuit reports as open.</summary>
        public bool IsOpen { get; set; }
        /// <summary>No-op success record.</summary>
        public void RecordSuccess()
        {
        }

        /// <summary>No-op failure record.</summary>
        public void RecordFailure()
        {
        }
    }

    /// <summary>Queue stub that records enqueued session ids.</summary>
    private sealed class FakeQueue : ITutorSessionQueue
    {
        /// <summary>Session ids passed to Enqueue.</summary>
        public List<Guid> Enqueued { get; } = new();
        /// <summary>Records the session id for later assertion.</summary>
        public void Enqueue(Guid sessionId) => Enqueued.Add(sessionId);
    }

    /// <summary>Queue stub that throws to simulate disabled background generation.</summary>
    private sealed class ThrowingQueue : ITutorSessionQueue
    {
        /// <summary>Throws InvalidOperationException on every enqueue.</summary>
        public void Enqueue(Guid sessionId)
            => throw new InvalidOperationException("Background tutor generation is disabled.");
    }

    /// <summary>Creates an in-memory ApplicationDbContext with the fixed test user.</summary>
    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options, new FakeCurrentUser());
    }

    /// <summary>Builds an AskTutorHandler with default fakes unless overrides are supplied.</summary>
    private static AskTutorHandler CreateAskHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeCurrentUser user,
        IRateLimiter? limiter = null,
        ITutorSessionQueue? queue = null,
        ITutorCircuit? circuit = null)
        => new(db, user, limiter ?? new InMemorySlidingWindowRateLimiter(),
            queue ?? new FakeQueue(), circuit ?? new FakeCircuit());

    /// <summary>Ten asks pass the sliding window; the eleventh throws RateLimitException with a positive retry-after.</summary>
    [Fact]
    public async Task RateLimit_TenOk_ElevenThrows()
    {
        var db = CreateDb();
        var user = new FakeCurrentUser();
        var limiter = new InMemorySlidingWindowRateLimiter(limit: 10);
        var handler = CreateAskHandler(db, user, limiter);

        for (var i = 0; i < 10; i++)
        {
            await handler.Handle(new AskTutorCommand($"Q{i}"), CancellationToken.None);
        }

        var ex = await Assert.ThrowsAsync<RateLimitException>(
            () => handler.Handle(new AskTutorCommand("Q11"), CancellationToken.None));
        Assert.True(ex.RetryAfterSeconds >= 1);
        Assert.Equal(10, await db.TutorSessions.CountAsync());
    }

    /// <summary>The rate limit window resets once a full hour has elapsed.</summary>
    [Fact]
    public async Task RateLimit_WindowResetsAfterHour()
    {
        var clock = new FakeClock();
        var limiter = new InMemorySlidingWindowRateLimiter(limit: 2, window: TimeSpan.FromHours(1), clock: clock);

        await limiter.CheckAsync("u");
        await limiter.CheckAsync("u");
        await Assert.ThrowsAsync<RateLimitException>(() => limiter.CheckAsync("u"));

        clock.Advance(TimeSpan.FromHours(1));
        await limiter.CheckAsync("u");
    }

    /// <summary>Ask creates a Pending session with question/user/timestamps, enqueues it, and returns a message containing the real session id.</summary>
    [Fact]
    public async Task Ask_CreatesPendingSession_ReturnsSessionId()
    {
        var db = CreateDb();
        var user = new FakeCurrentUser();
        var queue = new FakeQueue();
        var handler = CreateAskHandler(db, user, queue: queue);

        var result = await handler.Handle(new AskTutorCommand("Why is Kadane O(n)?"), CancellationToken.None);

        Assert.NotNull(result.Data);
        Assert.NotEqual(Guid.Empty, result.Data!.SessionId);
        Assert.Contains("Answer generation started", result.Data.Message);
        Assert.Contains(result.Data.SessionId.ToString(), result.Data.Message);
        Assert.DoesNotContain("{sessionId}", result.Data.Message);
        var session = await db.TutorSessions.SingleAsync(s => s.Id == result.Data.SessionId);
        Assert.Equal(TutorSessionStatus.Pending, session.Status);
        Assert.NotNull(session.QueuedAt);
        Assert.Equal("user-1", session.UserId);
        Assert.Equal("Why is Kadane O(n)?", session.Question);
        Assert.Equal(new[] { session.Id }, queue.Enqueued);
    }

    /// <summary>Open circuit short-circuits the ask with ServiceUnavailable and no session row is persisted.</summary>
    [Fact]
    public async Task Ask_CircuitOpen_ThrowsServiceUnavailable_NoSessionCreated()
    {
        var db = CreateDb();
        var handler = CreateAskHandler(db, new FakeCurrentUser(), circuit: new FakeCircuit { IsOpen = true });

        await Assert.ThrowsAsync<ServiceUnavailableException>(
            () => handler.Handle(new AskTutorCommand("q"), CancellationToken.None));
        Assert.Equal(0, await db.TutorSessions.CountAsync());
    }

    /// <summary>Queue failure during ask removes the created session row.</summary>
    [Fact]
    public async Task Ask_EnqueueFails_SessionRemoved()
    {
        var db = CreateDb();
        var handler = CreateAskHandler(db, new FakeCurrentUser(), queue: new ThrowingQueue());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new AskTutorCommand("q"), CancellationToken.None));
        Assert.Equal(0, await db.TutorSessions.CountAsync());
    }

    /// <summary>A non-positive rate limit throws ArgumentOutOfRangeException at construction.</summary>
    [Fact]
    public void RateLimit_NonPositiveLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemorySlidingWindowRateLimiter(limit: 0));
    }

    /// <summary>Rate limiter purges expired per-user windows when new checks run.</summary>
    [Fact]
    public async Task RateLimit_ExpiredKeysArePurged()
    {
        var clock = new FakeClock();
        var limiter = new InMemorySlidingWindowRateLimiter(limit: 2, clock: clock);

        await limiter.CheckAsync("stale");
        clock.Advance(TimeSpan.FromHours(2));
        await limiter.CheckAsync("fresh");

        var windows = (System.Collections.Concurrent.ConcurrentDictionary<string, Queue<DateTime>>)
            typeof(InMemorySlidingWindowRateLimiter)
                .GetField("_windows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(limiter)!;

        Assert.False(windows.ContainsKey("stale"));
        Assert.True(windows.ContainsKey("fresh"));
    }

    /// <summary>An unknown concept id makes Ask throw NotFoundException.</summary>
    [Fact]
    public async Task Ask_UnknownConcept_ThrowsNotFound()
    {
        var db = CreateDb();
        var handler = CreateAskHandler(db, new FakeCurrentUser());

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new AskTutorCommand("q", Guid.NewGuid()), CancellationToken.None));
    }

    /// <summary>A missing user id makes Ask throw UnauthorizedAccessException.</summary>
    [Fact]
    public async Task Ask_Unauthenticated_ThrowsUnauthorized()
    {
        var db = CreateDb();
        var handler = CreateAskHandler(db, new FakeCurrentUser { UserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => handler.Handle(new AskTutorCommand("q"), CancellationToken.None));
    }

    /// <summary>The session owner can poll a Pending session with no answer yet.</summary>
    [Fact]
    public async Task GetSession_ReturnsOwnedSession()
    {
        var db = CreateDb();
        var user = new FakeCurrentUser();
        var ask = CreateAskHandler(db, user);
        var created = await ask.Handle(new AskTutorCommand("hello"), CancellationToken.None);
        var get = new GetTutorSessionHandler(db, user);

        var result = await get.Handle(new GetTutorSessionQuery(created.Data!.SessionId), CancellationToken.None);

        Assert.Equal(created.Data.SessionId, result.Data!.SessionId);
        Assert.Equal(TutorSessionStatus.Pending, result.Data.Status);
        Assert.Null(result.Data.Answer);
    }

    /// <summary>Another user's session is invisible: GetSession throws NotFoundException.</summary>
    [Fact]
    public async Task GetSession_OtherUsersSession_ThrowsNotFound()
    {
        var db = CreateDb();
        var owner = new FakeCurrentUser { UserId = "owner" };
        var created = await CreateAskHandler(db, owner).Handle(new AskTutorCommand("secret"), CancellationToken.None);
        var other = new GetTutorSessionHandler(db, new FakeCurrentUser { UserId = "intruder" });

        await Assert.ThrowsAsync<NotFoundException>(
            () => other.Handle(new GetTutorSessionQuery(created.Data!.SessionId), CancellationToken.None));
    }

    /// <summary>A missing session id makes GetSession throw NotFoundException.</summary>
    [Fact]
    public async Task GetSession_Missing_ThrowsNotFound()
    {
        var db = CreateDb();
        var get = new GetTutorSessionHandler(db, new FakeCurrentUser());

        await Assert.ThrowsAsync<NotFoundException>(
            () => get.Handle(new GetTutorSessionQuery(Guid.NewGuid()), CancellationToken.None));
    }

    /// <summary>AskTutorValidator rejects a missing question.</summary>
    [Fact]
    public async Task Validator_MissingQuestion_Fails()
    {
        var validator = new AskTutorValidator();
        var result = await validator.ValidateAsync(new AskTutorCommand(""));
        Assert.False(result.IsValid);
    }

    /// <summary>AskTutorValidator rejects a question longer than 2000 characters.</summary>
    [Fact]
    public async Task Validator_QuestionOver2000_Fails()
    {
        var validator = new AskTutorValidator();
        var result = await validator.ValidateAsync(new AskTutorCommand(new string('x', 2001)));
        Assert.False(result.IsValid);
    }

    /// <summary>AskTutorValidator accepts a question of exactly 2000 characters.</summary>
    [Fact]
    public async Task Validator_QuestionAt2000_Passes()
    {
        var validator = new AskTutorValidator();
        var result = await validator.ValidateAsync(new AskTutorCommand(new string('x', 2000)));
        Assert.True(result.IsValid);
    }
}
