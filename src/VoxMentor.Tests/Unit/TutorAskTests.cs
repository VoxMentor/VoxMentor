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
    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options);
    }

    private static AskTutorHandler CreateAskHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeCurrentUser user,
        IRateLimiter? limiter = null)
        => new(db, user, limiter ?? new InMemorySlidingWindowRateLimiter());

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

    [Fact]
    public async Task Ask_CreatesPendingSession_ReturnsSessionId()
    {
        var db = CreateDb();
        var user = new FakeCurrentUser();
        var handler = CreateAskHandler(db, user);

        var result = await handler.Handle(new AskTutorCommand("Why is Kadane O(n)?"), CancellationToken.None);

        Assert.NotNull(result.Data);
        Assert.NotEqual(Guid.Empty, result.Data!.SessionId);
        Assert.DoesNotContain("being generated", result.Data.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pending", result.Data.Message);
        Assert.Contains(result.Data.SessionId.ToString(), result.Data.Message);
        Assert.DoesNotContain("{sessionId}", result.Data.Message);
        var session = await db.TutorSessions.SingleAsync(s => s.Id == result.Data.SessionId);
        Assert.Equal(TutorSessionStatus.Pending, session.Status);
        Assert.Equal("user-1", session.UserId);
        Assert.Equal("Why is Kadane O(n)?", session.Question);
    }

    [Fact]
    public void RateLimit_NonPositiveLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemorySlidingWindowRateLimiter(limit: 0));
    }

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

    [Fact]
    public async Task Ask_UnknownConcept_ThrowsNotFound()
    {
        var db = CreateDb();
        var handler = CreateAskHandler(db, new FakeCurrentUser());

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new AskTutorCommand("q", Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Ask_Unauthenticated_ThrowsUnauthorized()
    {
        var db = CreateDb();
        var handler = CreateAskHandler(db, new FakeCurrentUser { UserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => handler.Handle(new AskTutorCommand("q"), CancellationToken.None));
    }

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

    [Fact]
    public async Task GetSession_Missing_ThrowsNotFound()
    {
        var db = CreateDb();
        var get = new GetTutorSessionHandler(db, new FakeCurrentUser());

        await Assert.ThrowsAsync<NotFoundException>(
            () => get.Handle(new GetTutorSessionQuery(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Validator_MissingQuestion_Fails()
    {
        var validator = new AskTutorValidator();
        var result = await validator.ValidateAsync(new AskTutorCommand(""));
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validator_QuestionOver2000_Fails()
    {
        var validator = new AskTutorValidator();
        var result = await validator.ValidateAsync(new AskTutorCommand(new string('x', 2001)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validator_QuestionAt2000_Passes()
    {
        var validator = new AskTutorValidator();
        var result = await validator.ValidateAsync(new AskTutorCommand(new string('x', 2000)));
        Assert.True(result.IsValid);
    }
}
