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
        public string? Opening { get; set; }

        public Task<string?> OpeningAsync(
            string? company, string? role, string problemTitle, CancellationToken cancellationToken = default)
            => Task.FromResult(Opening);

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

    private sealed record TestApp(
        ServiceProvider Provider,
        ISender Sender,
        ApplicationDbContext Db,
        StubGenerator Generator) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private static TestApp CreateApp(string userId = "user-1", Dictionary<string, string?>? config = null)
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

        async Task<int> TryStartAsync()
        {
            using var scope = app.Provider.CreateAsyncScope();
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

        // Two starts in the same window: the locked re-count must let only one
        // through (CodeRabbit #146 daily-limit race).
        var outcomes = await Task.WhenAll(TryStartAsync(), TryStartAsync());

        Assert.Equal(1, outcomes.Count(o => o == 0));
        Assert.Equal(1, outcomes.Count(o => o == 1));
        Assert.Single(app.Db.MockInterviews.IgnoreQueryFilters().ToList());
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
