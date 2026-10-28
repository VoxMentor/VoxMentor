using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.MockInterviews;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="StartMockInterviewHandler"/>: daily-limit 429,
/// JD 404, selector call-through, generator fallback, and transcript seeding (#124).
/// </summary>
public class StartMockInterviewHandlerTests
{
    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
    }

    private sealed class StubGenerator : IMockInterviewGenerator
    {
        private int _calls;

        public string? Opening { get; set; }

        /// <summary>Held by the first caller until the test releases it (holds the start lock).</summary>
        public TaskCompletionSource? Gate { get; set; }

        /// <summary>Completes when any caller reaches generation.</summary>
        public TaskCompletionSource FirstCall { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => Volatile.Read(ref _calls);

        public async Task<string?> OpeningAsync(
            string? company, string? role, string problemTitle, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            FirstCall.TrySetResult();
            if (call == 1 && Gate is not null)
                await Gate.Task;
            return Opening;
        }

        public Task<string?> FollowUpAsync(
            IReadOnlyList<TranscriptTurn> transcript, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<MockInterviewReviewDraft?> ReviewAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            InterviewType type,
            string? company,
            string? role,
            CancellationToken cancellationToken = default)
            => Task.FromResult<MockInterviewReviewDraft?>(null);
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeClock(DateTimeOffset now) => _now = now;

        public void SetUtcNow(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed record TestApp(
        ServiceProvider Provider,
        ISender Sender,
        ApplicationDbContext Db,
        StubGenerator Generator) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private static TestApp CreateApp(string userId = "user-1", Dictionary<string, string?>? config = null, TimeProvider? clock = null)
    {
        var generator = new StubGenerator();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>())
            .Build();

        // Handler contexts come from the container (one new instance per scope)
        // so parallel sends in the daily-limit race test never share one EF
        // context; the outer db is only for seeding and assertions.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options, new FakeCurrentUserService { UserId = userId });

        var services = new ServiceCollection();
        services.AddScoped<IApplicationDbContext>(_ =>
            new ApplicationDbContext(options, new FakeCurrentUserService { UserId = userId }));
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService { UserId = userId });
        services.AddSingleton<IMockInterviewGenerator>(generator);
        services.AddSingleton<IConfiguration>(configuration);
        if (clock is not null)
            services.AddSingleton(clock);
        services.AddApplicationServices();

        var provider = services.BuildServiceProvider();
        return new TestApp(provider, provider.GetRequiredService<ISender>(), db, generator);
    }

    private static async Task<Concept> SeedConceptAsync(ApplicationDbContext db, string name = "Arrays")
    {
        var concept = new Concept
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = $"{name} desc",
            DifficultyLevel = 2,
            Category = "Data Structures"
        };
        db.Concepts.Add(concept);
        await db.SaveChangesAsync();
        return concept;
    }

    private static async Task<Question> SeedQuestionAsync(ApplicationDbContext db, Guid conceptId, string title = "Max Subarray Sum")
    {
        var question = new Question
        {
            Id = Guid.NewGuid(),
            ConceptId = conceptId,
            Title = title,
            Description = "Desc",
            QuestionType = "Code",
            Difficulty = 3,
            TestCases = new[] { "{\"input\":\"1\",\"expected\":\"1\"}" },
            HiddenTestCaseCount = 0
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        return question;
    }

    private static async Task<Guid> SeedJdAsync(ApplicationDbContext db, string userId, string company = "Acme", string role = "SDE-1")
    {
        var jd = new JobDescription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompanyName = company,
            Role = role,
            RawText = "Acme is hiring...",
            Difficulty = "Medium",
            EstimatedWeeks = 6
        };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();
        return jd.Id;
    }

    /// <summary>Starts an interview in its own DI scope: 0 = success, 1 = rate-limited.</summary>
    private static async Task<int> TryStartAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        try
        {
            await sender.Send(new StartMockInterviewCommand());
            return 0;
        }
        catch (RateLimitException)
        {
            return 1;
        }
    }

    [Fact]
    public async Task Start_DailyLimitReached_ThrowsRateLimitException()
    {
        using var app = CreateApp();
        var concept = await SeedConceptAsync(app.Db);
        await SeedQuestionAsync(app.Db, concept.Id);
        app.Db.MockInterviews.Add(new MockInterview
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            Type = InterviewType.Technical,
            TotalQuestions = 3,
            Status = MockInterviewStatus.InProgress,
            StartedAt = DateTime.UtcNow
        });
        await app.Db.SaveChangesAsync();

        // Default MockInterview:DailyLimit is 1 and one run already started today.
        await Assert.ThrowsAsync<RateLimitException>(
            () => app.Sender.Send(new StartMockInterviewCommand()));
    }

    [Fact]
    public async Task Start_UnknownJd_ThrowsNotFound()
    {
        using var app = CreateApp();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            app.Sender.Send(new StartMockInterviewCommand { JdId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Start_InvalidType_ThrowsValidationException()
    {
        using var app = CreateApp();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            app.Sender.Send(new StartMockInterviewCommand { Type = "Panel" }));

        Assert.Contains("Type", ex.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_NumericType_ThrowsValidationException()
    {
        using var app = CreateApp();

        // Enum.TryParse accepts "2" for an undefined member — must be rejected
        // so no bogus type is ever persisted (CodeRabbit #146).
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            app.Sender.Send(new StartMockInterviewCommand { Type = "2" }));

        Assert.Contains("Type", ex.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_ParallelStarts_ExactlyOneWins_DailyLimit()
    {
        using var app = CreateApp();
        var concept = await SeedConceptAsync(app.Db);
        await SeedQuestionAsync(app.Db, concept.Id);

        // Deterministic overlap (CodeRabbit #146): the first start is held
        // inside the lock at generation while the second attempt runs its
        // advisory count and blocks on the lock. Releasing then lets the loser
        // re-count and fail — without ever reaching generation. Asserting a
        // single generation call makes the test fail if the re-count order or
        // the per-student lock is ever removed (unlike a bare Task.WhenAll,
        // which can pass sequentially).
        app.Generator.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = TryStartAsync(app.Provider);
        await app.Generator.FirstCall.Task;
        var second = TryStartAsync(app.Provider);
        await Task.Delay(50); // give the loser time to reach the lock before release
        app.Generator.Gate.SetResult();

        var outcomes = await Task.WhenAll(first, second);

        Assert.Equal(1, outcomes.Count(o => o == 0));
        Assert.Equal(1, outcomes.Count(o => o == 1));
        Assert.Equal(1, app.Generator.CallCount);
        Assert.Single(app.Db.MockInterviews.IgnoreQueryFilters().ToList());
    }

    [Fact]
    public async Task Start_AfterUtcMidnight_NewQuotaDay_AndStampMatchesClock()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 26, 23, 59, 0, TimeSpan.Zero));
        using var app = CreateApp(clock: clock);
        var concept = await SeedConceptAsync(app.Db);
        await SeedQuestionAsync(app.Db, concept.Id);
        app.Db.MockInterviews.Add(new MockInterview
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            Type = InterviewType.Technical,
            TotalQuestions = 3,
            Status = MockInterviewStatus.InProgress,
            StartedAt = clock.GetUtcNow().UtcDateTime // already used today's quota
        });
        await app.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<RateLimitException>(
            () => app.Sender.Send(new StartMockInterviewCommand()));

        // Crossing UTC midnight resets the quota; count and StartedAt must use
        // the same clock reading (CodeRabbit #146 midnight boundary).
        clock.SetUtcNow(new DateTimeOffset(2026, 10, 27, 0, 0, 1, TimeSpan.Zero));

        var result = await app.Sender.Send(new StartMockInterviewCommand());

        Assert.True(result.Success);
        Assert.Equal(new DateTime(2026, 10, 27), result.Data!.StartedAt.Date);
        Assert.Equal(2, await app.Db.MockInterviews.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Start_MidnightCrossingWhileHeld_LockRecountUsesNewDay()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 26, 23, 59, 30, TimeSpan.Zero));
        using var app = CreateApp(clock: clock);
        var concept = await SeedConceptAsync(app.Db);
        await SeedQuestionAsync(app.Db, concept.Id);

        // Regression for stale day bounds inside the lock (CodeRabbit #146):
        // hold the first start at generation pre-midnight (it will stamp and
        // count the old day), let the second pass its advisory count for the
        // old day and block on the lock, then cross UTC midnight before
        // release. The in-lock re-count must recompute the day — the second
        // start succeeds on the new day with two generation calls; stale
        // bounds would count the first start's pre-midnight row and 429.
        app.Generator.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = TryStartAsync(app.Provider);
        await app.Generator.FirstCall.Task;
        var second = TryStartAsync(app.Provider);
        await Task.Delay(50); // second passes the old-day advisory count and blocks on the lock
        clock.SetUtcNow(new DateTimeOffset(2026, 10, 27, 0, 0, 1, TimeSpan.Zero));
        app.Generator.Gate.SetResult();

        Assert.Equal(0, await first);
        Assert.Equal(0, await second);
        Assert.Equal(2, app.Generator.CallCount);

        var interviews = await app.Db.MockInterviews.IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(m => m.StartedAt)
            .ToListAsync();
        Assert.Equal(2, interviews.Count);
        Assert.Equal(new DateTime(2026, 10, 26), interviews[0].StartedAt.Date);
        Assert.Equal(new DateTime(2026, 10, 27), interviews[1].StartedAt.Date);
    }

    [Fact]
    public async Task Start_WithoutJd_GeneratorUnavailable_FallsBackToCannedOpening()
    {
        using var app = CreateApp();
        var concept = await SeedConceptAsync(app.Db);
        var question = await SeedQuestionAsync(app.Db, concept.Id);
        app.Generator.Opening = null;

        var result = await app.Sender.Send(new StartMockInterviewCommand());

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains("your AI interviewer today", result.Data!.OpeningMessage, StringComparison.Ordinal);
        Assert.Contains(question.Title, result.Data.OpeningMessage, StringComparison.Ordinal);

        var interview = await app.Db.MockInterviews.IgnoreQueryFilters()
            .SingleAsync(m => m.Id == result.Data.InterviewId);
        Assert.Equal(MockInterviewStatus.InProgress, interview.Status);
        Assert.Null(interview.JdId);
        Assert.Equal(3, interview.TotalQuestions);

        var turns = System.Text.Json.JsonSerializer.Deserialize<List<TranscriptTurn>>(
            interview.TranscriptJson!, TranscriptTurn.JsonOptions);
        Assert.NotNull(turns);
        var opening = Assert.Single(turns);
        Assert.Equal(TranscriptTurn.InterviewerRole, opening.Role);
        Assert.Equal(result.Data.OpeningMessage, opening.Message);
        Assert.Equal(question.Id, opening.QuestionId);
        Assert.Equal(question.Id, result.Data.QuestionId);
    }

    [Fact]
    public async Task Start_WithJd_GeneratorUnavailable_FallsBackToCompanyOpening()
    {
        using var app = CreateApp();
        var concept = await SeedConceptAsync(app.Db);
        await SeedQuestionAsync(app.Db, concept.Id);
        var jdId = await SeedJdAsync(app.Db, "user-1", company: "Acme", role: "SDE-1");
        app.Generator.Opening = null;

        var result = await app.Sender.Send(new StartMockInterviewCommand { JdId = jdId });

        Assert.NotNull(result.Data);
        Assert.Contains("at Acme for the SDE-1 position", result.Data!.OpeningMessage, StringComparison.Ordinal);

        var interview = await app.Db.MockInterviews.IgnoreQueryFilters()
            .SingleAsync(m => m.Id == result.Data.InterviewId);
        Assert.Equal(jdId, interview.JdId);
    }

    [Fact]
    public async Task Start_GeneratorLine_UsedVerbatim_AndPersistedAsFirstTurn()
    {
        using var app = CreateApp();
        var concept = await SeedConceptAsync(app.Db);
        await SeedQuestionAsync(app.Db, concept.Id);
        app.Generator.Opening = "GENERATED OPENING";

        var result = await app.Sender.Send(new StartMockInterviewCommand());

        Assert.NotNull(result.Data);
        Assert.Equal("GENERATED OPENING", result.Data!.OpeningMessage);
        Assert.Equal("Technical", result.Data.Type);
        Assert.Equal("InProgress", result.Data.Status);
        Assert.Equal(45, result.Data.Duration);
    }
}
