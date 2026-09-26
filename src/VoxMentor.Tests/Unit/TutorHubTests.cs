using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxMentor.Api.Hubs;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.RateLimiting;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Coverage for TutorHub streaming (#73): token fan-out, TutorComplete payload,
/// session persistence, failure path, and hub-side rate limiting.
/// </summary>
public class TutorHubTests
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

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
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

    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options);
    }

    private static (TutorHub Hub, FakeClients Clients, Infrastructure.Persistence.ApplicationDbContext Db) CreateHub(
        ITutorService? tutor = null,
        IRateLimiter? limiter = null)
    {
        var db = CreateDb();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<TutorHub>();
        var hub = new TutorHub(
            tutor ?? new FakeTutorService(),
            db,
            limiter ?? new InMemorySlidingWindowRateLimiter(),
            new FakeCurrentUser(),
            logger)
        {
            Context = new FakeHubContext(),
            Clients = new FakeClients()
        };
        return (hub, (FakeClients)hub.Clients, db);
    }

    [Fact]
    public async Task AskTutor_StreamsTokens_SendsComplete_PersistsCompletedSession()
    {
        var (hub, clients, db) = CreateHub();

        await hub.AskTutor("", "Why is Kadane O(n)?");

        var sent = clients.CallerProxy.Sent;
        Assert.Equal(new[] { "TutorToken", "TutorToken", "TutorComplete" }, sent.Select(s => s.Method));

        var complete = sent.Single(s => s.Method == "TutorComplete").Args[0]!;
        var payloadType = complete.GetType();
        var sessionId = (Guid)payloadType.GetProperty("sessionId")!.GetValue(complete)!;
        var totalTokens = (int)payloadType.GetProperty("totalTokens")!.GetValue(complete)!;
        Assert.Equal(42, totalTokens);

        var session = await db.TutorSessions.SingleAsync();
        Assert.Equal(sessionId, session.Id);
        Assert.Equal(TutorSessionStatus.Completed, session.Status);
        Assert.Equal("Hello", session.Answer);
        Assert.Equal(42, session.TotalTokens);
        Assert.NotNull(session.CompletedAt);
    }

    [Fact]
    public async Task AskTutor_ServiceThrows_SendsError_MarksSessionFailed()
    {
        var (hub, clients, db) = CreateHub(
            new FakeTutorService { Throw = new InvalidOperationException("Ollama down") });

        await hub.AskTutor("", "question?");

        var sent = clients.CallerProxy.Sent;
        Assert.Contains(sent, s => s.Method == "TutorError");
        var error = sent.Single(s => s.Method == "TutorError").Args[0]!;
        Assert.Equal("An error occurred while generating the response.", 
            error.GetType().GetProperty("message")!.GetValue(error)!.ToString());

        var session = await db.TutorSessions.SingleAsync();
        Assert.Equal(TutorSessionStatus.Failed, session.Status);
        Assert.NotNull(session.CompletedAt);
    }

    [Fact]
    public async Task AskTutor_RateLimited_SendsError_NoSessionPersisted()
    {
        var (hub, clients, db) = CreateHub(
            limiter: new InMemorySlidingWindowRateLimiter(limit: 1));

        await hub.AskTutor("", "first");
        await hub.AskTutor("", "second");

        Assert.Contains(clients.CallerProxy.Sent, s =>
            s.Method == "TutorError" &&
            s.Args[0]!.ToString() != null &&
            s.Args[0]!.GetType().GetProperty("message")!.GetValue(s.Args[0])!.ToString()!.Contains("Rate limit"));
        Assert.Single(await db.TutorSessions.ToListAsync());
    }

    [Fact]
    public async Task AskTutor_EmptyQuestion_SendsError()
    {
        var (hub, clients, db) = CreateHub();

        await hub.AskTutor("", "   ");

        Assert.Contains(clients.CallerProxy.Sent, s => s.Method == "TutorError");
        Assert.Empty(await db.TutorSessions.ToListAsync());
    }

    [Fact]
    public async Task AskTutor_InvalidGuid_SendsError()
    {
        var (hub, clients, db) = CreateHub();

        await hub.AskTutor("not-a-guid", "question?");

        Assert.Contains(clients.CallerProxy.Sent, s => s.Method == "TutorError");
        var error = clients.CallerProxy.Sent.Single(s => s.Method == "TutorError").Args[0]!;
        var message = error.GetType().GetProperty("message")!.GetValue(error)!.ToString();
        Assert.Contains("Invalid conceptId", message);
        Assert.Empty(await db.TutorSessions.ToListAsync());
    }

    [Fact]
    public async Task AskTutor_ConceptNotFound_SendsError()
    {
        var (hub, clients, db) = CreateHub();

        await hub.AskTutor(Guid.NewGuid().ToString(), "question?");

        Assert.Contains(clients.CallerProxy.Sent, s => s.Method == "TutorError");
        var error = clients.CallerProxy.Sent.Single(s => s.Method == "TutorError").Args[0]!;
        var message = error.GetType().GetProperty("message")!.GetValue(error)!.ToString();
        Assert.Contains("Concept", message);
        Assert.Contains("was not found", message);
        Assert.Empty(await db.TutorSessions.ToListAsync());
    }

    [Fact]
    public async Task AskTutor_EmptyStream_CompletesWithZeroTokens()
    {
        var fakeTutor = new FakeTutorService
        {
            Chunks = new List<TutorChunk> { new("Hello", false), new(" World", false) }
        };
        var (hub, clients, db) = CreateHub(fakeTutor);

        await hub.AskTutor("", "question?");

        var sent = clients.CallerProxy.Sent;
        Assert.Contains(sent, s => s.Method == "TutorComplete");
        var complete = sent.Single(s => s.Method == "TutorComplete").Args[0]!;
        var totalTokens = (int)complete.GetType().GetProperty("totalTokens")!.GetValue(complete)!;
        Assert.Equal(0, totalTokens);

        var session = await db.TutorSessions.SingleAsync();
        Assert.Equal(TutorSessionStatus.Completed, session.Status);
        Assert.Equal(0, session.TotalTokens);
    }

    [Fact]
    public async Task AskTutor_SecondAskWhileStreaming_Rejected_NoSecondSession()
    {
        var gate = new TaskCompletionSource();
        var (hub, clients, db) = CreateHub(new BlockingTutorService(gate.Task));

        var first = hub.AskTutor("", "first question");
        await Task.Delay(50); // let the first ask reach the stream

        await hub.AskTutor("", "second question");

        var errors = clients.CallerProxy.Sent.Where(s => s.Method == "TutorError").ToList();
        Assert.Single(errors);
        var msg = errors[0].Args[0]!.GetType().GetProperty("message")!.GetValue(errors[0].Args[0])!.ToString();
        Assert.Contains("already have a tutor answer in progress", msg);

        gate.SetResult();
        await first;

        Assert.Single(await db.TutorSessions.ToListAsync());
        Assert.Contains(clients.CallerProxy.Sent, s => s.Method == "TutorComplete");
    }

    [Fact]
    public async Task AskTutor_AfterPreviousCompletes_Succeeds()
    {
        var (hub, clients, db) = CreateHub();

        await hub.AskTutor("", "first");
        await hub.AskTutor("", "second");

        Assert.Contains(clients.CallerProxy.Sent, s => s.Method == "TutorComplete");
        Assert.Equal(2, await db.TutorSessions.CountAsync());
    }

    private sealed class BlockingTutorService : ITutorService
    {
        private readonly Task _block;
        public BlockingTutorService(Task block) => _block = block;

        public async IAsyncEnumerable<TutorChunk> StreamAnswerAsync(
            string question,
            Guid? conceptId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await _block;
            yield return new TutorChunk("done", true, 1);
        }
    }
}
