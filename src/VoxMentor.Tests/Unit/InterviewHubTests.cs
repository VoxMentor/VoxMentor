using System.Runtime.CompilerServices;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VoxMentor.Api.Hubs;
using VoxMentor.Application;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.MockInterviews;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Coverage for InterviewHub streaming (#126): tokens fan-out, persisted rows
/// visible to the history query, InterviewComplete with the real score,
/// InterviewError on failure, and validation guards.
/// </summary>
public class InterviewHubTests
{
    private sealed class StubGenerator : IMockInterviewGenerator
    {
        public List<TutorChunk> OpeningChunks { get; set; } = new() { new("Hel"), new("lo") };
        public List<TutorChunk> FollowUpChunks { get; set; } = new() { new("Probe "), new("one?") };
        public MockInterviewReviewDraft? Review { get; set; } = new(
            88f,
            new Dictionary<string, ProblemReview>(StringComparer.Ordinal)
            {
                ["problem1"] = new ProblemReview(Solved: true, "Optimal", "Solid.")
            },
            "Keep practicing.");
        public Exception? OpeningThrow { get; set; }
        public Exception? FollowUpThrow { get; set; }

        public async IAsyncEnumerable<TutorChunk> OpeningStreamAsync(
            string? company, string? role, string problemTitle,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (OpeningThrow is not null)
            {
                throw OpeningThrow;
            }
            foreach (var chunk in OpeningChunks)
            {
                yield return chunk;
            }
        }

        public async IAsyncEnumerable<TutorChunk> FollowUpStreamAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (FollowUpThrow is not null)
            {
                throw FollowUpThrow;
            }
            foreach (var chunk in FollowUpChunks)
            {
                yield return chunk;
            }
        }

        public Task<bool?> AssessAnswerSolvedAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            InterviewType type,
            CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(false);

        public Task<MockInterviewReviewDraft?> ReviewAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            InterviewType type,
            string? company,
            string? role,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Review);
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => _stopping.Cancel();
    }

    private sealed class RecordingProxy : IClientProxy
    {
        public List<(string Method, object?[] Args)> Sent { get; } = new();

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Sent.Add((method, args));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubContext : HubCallerContext
    {
        public override string ConnectionId => "conn-1";
        public override string? UserIdentifier => "user-1";
        public override ClaimsPrincipal User { get; } = new(new ClaimsIdentity());
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class FakeClients : IHubCallerClients
    {
        public RecordingProxy CallerProxy { get; } = new();
        public IClientProxy All => CallerProxy;
        public IClientProxy Caller => CallerProxy;
        public IClientProxy Others => CallerProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => CallerProxy;
        public IClientProxy Client(string connectionId) => CallerProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => CallerProxy;
        public IClientProxy Group(string groupName) => CallerProxy;
        public IClientProxy OthersInGroup(string groupName) => CallerProxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => CallerProxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => CallerProxy;
        public IClientProxy User(string userId) => CallerProxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => CallerProxy;
    }

    private sealed record TestApp(
        ServiceProvider Provider,
        IServiceScope Scope,
        ISender Sender,
        ApplicationDbContext Db,
        StubGenerator Generator) : IDisposable
    {
        public void Dispose()
        {
            Scope.Dispose();
            Provider.Dispose();
        }
    }

    private static TestApp CreateApp(string userId = "user-1", StubGenerator? generator = null)
    {
        generator ??= new StubGenerator();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();
        services.AddScoped<IApplicationDbContext>(_ =>
            new ApplicationDbContext(options, new FakeCurrentUser { UserId = userId }));
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUser { UserId = userId });
        services.AddSingleton<IMockInterviewGenerator>(generator);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddScoped<IMasteryEventPublisher, NullMasteryEventPublisher>();
        services.AddApplicationServices();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new TestApp(
            provider,
            scope,
            scope.ServiceProvider.GetRequiredService<ISender>(),
            new ApplicationDbContext(options, new FakeCurrentUser { UserId = userId }),
            generator);
    }

    private static (InterviewHub Hub, FakeClients Clients) CreateHub(TestApp app, FakeLifetime? lifetime = null)
    {
        var sp = app.Scope.ServiceProvider;
        var hub = new InterviewHub(
            sp.GetRequiredService<MockInterviewSessionService>(),
            app.Sender,
            sp.GetRequiredService<ICurrentUserService>(),
            sp.GetRequiredService<IApplicationDbContext>(),
            lifetime ?? new FakeLifetime(),
            sp.GetRequiredService<ILogger<InterviewHub>>(),
            sp.GetRequiredService<FluentValidation.IValidator<StartMockInterviewCommand>>(),
            sp.GetRequiredService<FluentValidation.IValidator<AnswerMockInterviewCommand>>())
        {
            Context = new FakeHubContext(),
            Clients = new FakeClients()
        };
        return (hub, (FakeClients)hub.Clients);
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

    private static async Task<MockInterview> SeedInterviewAsync(
        ApplicationDbContext db,
        string userId = "user-1",
        MockInterviewStatus status = MockInterviewStatus.InProgress)
    {
        var interview = new MockInterview
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = InterviewType.Technical,
            TotalQuestions = 3,
            Status = status,
            StartedAt = DateTime.UtcNow,
            TranscriptJson = System.Text.Json.JsonSerializer.Serialize(
                new List<TranscriptTurn>
                {
                    new(TranscriptTurn.InterviewerRole, "Opening problem.", Guid.NewGuid(), DateTime.UtcNow)
                },
                TranscriptTurn.JsonOptions)
        };
        db.MockInterviews.Add(interview);
        await db.SaveChangesAsync();
        return interview;
    }

    [Fact]
    public async Task StartMock_StreamsTokens_PersistsRow_VisibleToHistory()
    {
        using var app = CreateApp();
        var concept = await SeedConceptAsync(app.Db);
        await SeedQuestionAsync(app.Db, concept.Id);
        var (hub, clients) = CreateHub(app);

        await hub.StartMock(null, "Technical", voiceMode: false);

        var sent = clients.CallerProxy.Sent;
        Assert.Equal(new[] { "InterviewerMessage", "InterviewerMessage" }, sent.Select(s => s.Method));
        Assert.Equal("Hel", sent[0].Args[0]);
        Assert.Equal("lo", sent[1].Args[0]);

        var interview = await app.Db.MockInterviews.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(MockInterviewStatus.InProgress, interview.Status);

        var history = await app.Sender.Send(new GetMockInterviewHistoryQuery());
        Assert.True(history.Success);
        var item = Assert.Single(history.Data!.Interviews);
        Assert.Equal(interview.Id, item.InterviewId);
        Assert.Equal("InProgress", item.Status);
    }

    [Fact]
    public async Task StartMock_GeneratorThrows_SendsInterviewError_NoRowPersisted()
    {
        using var app = CreateApp(generator: new StubGenerator
        {
            OpeningThrow = new Application.Common.Exceptions.MockInterviewGenerationException("Ollama down")
        });
        var concept = await SeedConceptAsync(app.Db);
        await SeedQuestionAsync(app.Db, concept.Id);
        var (hub, clients) = CreateHub(app);

        await hub.StartMock(null, "Technical", voiceMode: false);

        var error = Assert.Single(clients.CallerProxy.Sent, s => s.Method == "InterviewError");
        var message = error.Args[0]!.GetType().GetProperty("message")!.GetValue(error.Args[0])!.ToString();
        Assert.Contains("temporarily unavailable", message);
        Assert.Empty(await app.Db.MockInterviews.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task StartMock_DailyLimitReached_SendsInterviewError()
    {
        using var app = CreateApp();
        var concept = await SeedConceptAsync(app.Db);
        await SeedQuestionAsync(app.Db, concept.Id);
        await SeedInterviewAsync(app.Db); // one run already started today, limit default 1
        var (hub, clients) = CreateHub(app);

        await hub.StartMock(null, "Technical", voiceMode: false);

        var error = Assert.Single(clients.CallerProxy.Sent, s => s.Method == "InterviewError");
        var message = error.Args[0]!.GetType().GetProperty("message")!.GetValue(error.Args[0])!.ToString();
        Assert.Contains("Rate limit", message);
        Assert.Single(await app.Db.MockInterviews.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task StartMock_UnknownJd_SendsInterviewError()
    {
        using var app = CreateApp();
        var (hub, clients) = CreateHub(app);

        await hub.StartMock(Guid.NewGuid().ToString(), "Technical", voiceMode: false);

        var error = Assert.Single(clients.CallerProxy.Sent, s => s.Method == "InterviewError");
        var message = error.Args[0]!.GetType().GetProperty("message")!.GetValue(error.Args[0])!.ToString();
        Assert.Contains("not found", message);
    }

    [Fact]
    public async Task StartMock_InvalidType_SendsInterviewError()
    {
        using var app = CreateApp();
        var (hub, clients) = CreateHub(app);

        await hub.StartMock(null, "Panel", voiceMode: false);

        var error = Assert.Single(clients.CallerProxy.Sent, s => s.Method == "InterviewError");
        var message = error.Args[0]!.GetType().GetProperty("message")!.GetValue(error.Args[0])!.ToString();
        Assert.Contains("Technical", message);
    }

    [Fact]
    public async Task SendAnswer_UnknownInterview_SendsInterviewError()
    {
        using var app = CreateApp();
        var (hub, clients) = CreateHub(app);

        await hub.SendAnswer(Guid.NewGuid().ToString(), "my answer");

        var error = Assert.Single(clients.CallerProxy.Sent, s => s.Method == "InterviewError");
        var message = error.Args[0]!.GetType().GetProperty("message")!.GetValue(error.Args[0])!.ToString();
        Assert.Contains("not found", message);
    }

    [Fact]
    public async Task SendAnswer_EndedInterview_SendsInterviewError()
    {
        using var app = CreateApp();
        var interview = await SeedInterviewAsync(app.Db, status: MockInterviewStatus.Completed);
        var (hub, clients) = CreateHub(app);

        await hub.SendAnswer(interview.Id.ToString(), "my answer");

        var error = Assert.Single(clients.CallerProxy.Sent, s => s.Method == "InterviewError");
        var message = error.Args[0]!.GetType().GetProperty("message")!.GetValue(error.Args[0])!.ToString();
        Assert.Contains("already ended", message);
    }

    [Fact]
    public async Task SendAnswer_EmptyMessage_SendsInterviewError()
    {
        using var app = CreateApp();
        var interview = await SeedInterviewAsync(app.Db);
        var (hub, clients) = CreateHub(app);

        await hub.SendAnswer(interview.Id.ToString(), "   ");

        var error = Assert.Single(clients.CallerProxy.Sent, s => s.Method == "InterviewError");
        var message = error.Args[0]!.GetType().GetProperty("message")!.GetValue(error.Args[0])!.ToString();
        Assert.Contains("required", message);

        app.Db.ChangeTracker.Clear();
        var stored = await app.Db.MockInterviews.IgnoreQueryFilters().SingleAsync(m => m.Id == interview.Id);
        Assert.Single(System.Text.Json.JsonSerializer.Deserialize<List<TranscriptTurn>>(
            stored.TranscriptJson!, TranscriptTurn.JsonOptions)!);
    }

    [Fact]
    public async Task SendAnswer_StreamsProbeTokens_AndPersistsTurns()
    {
        using var app = CreateApp();
        var interview = await SeedInterviewAsync(app.Db);
        var (hub, clients) = CreateHub(app);

        await hub.SendAnswer(interview.Id.ToString(), "Kadane's algorithm");

        var sent = clients.CallerProxy.Sent;
        Assert.Equal(new[] { "InterviewerMessage", "InterviewerMessage" }, sent.Select(s => s.Method));
        Assert.Equal("Probe ", sent[0].Args[0]);
        Assert.Equal("one?", sent[1].Args[0]);

        // The hub saves through its scoped context; drop our tracked copy so
        // the verification reads hit the shared store, not the cache.
        app.Db.ChangeTracker.Clear();
        var stored = await app.Db.MockInterviews.IgnoreQueryFilters().SingleAsync(m => m.Id == interview.Id);
        var turns = System.Text.Json.JsonSerializer.Deserialize<List<TranscriptTurn>>(
            stored.TranscriptJson!, TranscriptTurn.JsonOptions)!;
        Assert.Equal(3, turns.Count);
        Assert.Equal(TranscriptTurn.CandidateRole, turns[1].Role);
        Assert.Equal("Kadane's algorithm", turns[1].Message);
        Assert.Equal(TranscriptTurn.InterviewerRole, turns[2].Role);
        Assert.Equal("Probe one?", turns[2].Message);
    }

    [Fact]
    public async Task SendAnswer_GeneratorThrows_SendsInterviewError_NoTurnPersisted()
    {
        using var app = CreateApp(generator: new StubGenerator
        {
            FollowUpThrow = new Application.Common.Exceptions.MockInterviewGenerationException("Ollama down")
        });
        var interview = await SeedInterviewAsync(app.Db);
        var (hub, clients) = CreateHub(app);

        await hub.SendAnswer(interview.Id.ToString(), "Kadane's algorithm");

        var error = Assert.Single(clients.CallerProxy.Sent, s => s.Method == "InterviewError");
        var message = error.Args[0]!.GetType().GetProperty("message")!.GetValue(error.Args[0])!.ToString();
        Assert.Contains("temporarily unavailable", message);

        app.Db.ChangeTracker.Clear();
        var stored = await app.Db.MockInterviews.IgnoreQueryFilters().SingleAsync(m => m.Id == interview.Id);
        Assert.Single(System.Text.Json.JsonSerializer.Deserialize<List<TranscriptTurn>>(
            stored.TranscriptJson!, TranscriptTurn.JsonOptions)!);
    }

    [Fact]
    public async Task CompleteMock_EmitsRealScore_PersistsCompleted()
    {
        using var app = CreateApp();
        var interview = await SeedInterviewAsync(app.Db);
        var (hub, clients) = CreateHub(app);

        await hub.CompleteMock(interview.Id.ToString());

        var complete = Assert.Single(clients.CallerProxy.Sent, s => s.Method == "InterviewComplete");
        var payload = complete.Args[0]!;
        var score = (float)payload.GetType().GetProperty("score")!.GetValue(payload)!;
        var status = payload.GetType().GetProperty("status")!.GetValue(payload)!.ToString();
        Assert.Equal(88f, score);
        Assert.Equal("Completed", status);

        app.Db.ChangeTracker.Clear();
        var stored = await app.Db.MockInterviews.IgnoreQueryFilters().SingleAsync(m => m.Id == interview.Id);
        Assert.Equal(MockInterviewStatus.Completed, stored.Status);
        Assert.Equal(88f, stored.Score);
    }

    [Fact]
    public async Task CompleteMock_UnknownInterview_SendsInterviewError()
    {
        using var app = CreateApp();
        var (hub, clients) = CreateHub(app);

        await hub.CompleteMock(Guid.NewGuid().ToString());

        var error = Assert.Single(clients.CallerProxy.Sent, s => s.Method == "InterviewError");
        var message = error.Args[0]!.GetType().GetProperty("message")!.GetValue(error.Args[0])!.ToString();
        Assert.Contains("not found", message);
    }

    [Fact]
    public async Task CompleteMock_InvalidId_SendsInterviewError()
    {
        using var app = CreateApp();
        var (hub, clients) = CreateHub(app);

        await hub.CompleteMock("not-a-guid");

        var error = Assert.Single(clients.CallerProxy.Sent, s => s.Method == "InterviewError");
        var message = error.Args[0]!.GetType().GetProperty("message")!.GetValue(error.Args[0])!.ToString();
        Assert.Contains("Invalid interviewId", message);
    }
}
