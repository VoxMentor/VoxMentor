using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxMentor.Api.Hubs;
using VoxMentor.Api.Services;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;
using Xunit;

namespace VoxMentor.Tests.Unit;

public class SignalRMasteryEventPublisherTests
{
    private sealed class RecordingProxy : IClientProxy
    {
        public List<(string Method, object?[] Args)> Sent { get; } = new();
        public Func<string, object?[], CancellationToken, Task>? OnSendCoreAsync { get; set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Sent.Add((method, args));
            if (OnSendCoreAsync is not null)
            {
                return OnSendCoreAsync(method, args, cancellationToken);
            }
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

    private sealed class FakeHubClients : IHubClients<MasteryHub>, IHubClients
    {
        public RecordingProxy CallerProxy { get; } = new();
        public string? RequestedUserId { get; private set; }

        // IHubCallerClients members (from IHubClients)
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
        public IClientProxy User(string userId)
        {
            RequestedUserId = userId;
            return CallerProxy;
        }
        public IClientProxy Users(IReadOnlyList<string> userIds) => CallerProxy;

        // IHubClients<MasteryHub> strongly-typed members (explicit implementation for hiding)
        MasteryHub IHubClients<MasteryHub>.All => throw new NotImplementedException();
        MasteryHub IHubClients<MasteryHub>.Client(string connectionId) => throw new NotImplementedException();
        MasteryHub IHubClients<MasteryHub>.Group(string groupName) => throw new NotImplementedException();
        MasteryHub IHubClients<MasteryHub>.Groups(IReadOnlyList<string> groupNames) => throw new NotImplementedException();
        MasteryHub IHubClients<MasteryHub>.User(string userId) => throw new NotImplementedException();
        MasteryHub IHubClients<MasteryHub>.Users(IReadOnlyList<string> userIds) => throw new NotImplementedException();
        MasteryHub IHubClients<MasteryHub>.AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
        MasteryHub IHubClients<MasteryHub>.GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
        MasteryHub IHubClients<MasteryHub>.Clients(IReadOnlyList<string> connectionIds) => throw new NotImplementedException();
    }

    private sealed class FakeHubContextAccessor : IHubContext<MasteryHub>
    {
        private readonly FakeHubClients _clients;

        public FakeHubContextAccessor(FakeHubClients clients)
        {
            _clients = clients;
        }

        public IHubClients Clients => _clients;
        public IGroupManager Groups => throw new NotImplementedException();
    }

    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options);
    }

    [Fact]
    public async Task PublishMasteryUpdatedAsync_SendsMasteryUpdatedToCorrectUser()
    {
        var db = CreateDb();
        var concept = new Concept { Id = Guid.NewGuid(), Name = "Kadane's Algorithm", Description = "desc" };
        var mastery = new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = "user-123",
            ConceptId = concept.Id,
            MasteryProbability = 0.85f
        };
        db.Concepts.Add(concept);
        db.StudentMasteries.Add(mastery);
        await db.SaveChangesAsync();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SignalRMasteryEventPublisher>();

        var clients = new FakeHubClients();
        var hubContext = new FakeHubContextAccessor(clients);

        var publisher = new SignalRMasteryEventPublisher(hubContext, db, logger);

        await publisher.PublishMasteryUpdatedAsync(mastery, 0.5f, CancellationToken.None);

        Assert.Equal("user-123", clients.RequestedUserId);
        var sent = clients.CallerProxy.Sent;
        Assert.Contains(sent, s => s.Method == "MasteryUpdated");
        var masterUpdated = sent.Single(s => s.Method == "MasteryUpdated");
        Assert.True(masterUpdated.Args.Length > 0, "MasteryUpdated should have at least one argument");
        var payload = masterUpdated.Args[0]!;
        var payloadType = payload.GetType();
        var props = payloadType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var propNames = props.Select(p => p.Name).ToArray();
        Assert.Contains(propNames, p => string.Equals(p, "conceptId", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(propNames, p => string.Equals(p, "conceptName", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(propNames, p => string.Equals(p, "newMastery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(propNames, p => string.Equals(p, "delta", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(concept.Id, (Guid)payloadType.GetProperty("conceptId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)!.GetValue(payload)!);
        Assert.Equal("Kadane's Algorithm", (string)payloadType.GetProperty("conceptName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)!.GetValue(payload)!);
        var actualNewMastery = (float)payloadType.GetProperty("newMastery", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)!.GetValue(payload)!;
        Assert.True(Math.Abs(actualNewMastery - mastery.MasteryProbability) < 0.0001f, $"newMastery {actualNewMastery} != expected {mastery.MasteryProbability}");
        var actualDelta = (float)payloadType.GetProperty("delta", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)!.GetValue(payload)!;
        var expectedDelta = mastery.MasteryProbability - 0.5f;
        Assert.True(Math.Abs(actualDelta - expectedDelta) < 0.0001f, $"delta {actualDelta} != expected {expectedDelta}");
    }

    [Fact]
    public async Task PublishMasteryUpdatedAsync_ConceptDeleted_SendsEmptyConceptName()
    {
        var db = CreateDb();
        var conceptId = Guid.NewGuid();
        var mastery = new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = "user-456",
            ConceptId = conceptId,
            MasteryProbability = 0.3f
        };
        db.StudentMasteries.Add(mastery);
        await db.SaveChangesAsync();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SignalRMasteryEventPublisher>();

        var clients = new FakeHubClients();
        var hubContext = new FakeHubContextAccessor(clients);

        var publisher = new SignalRMasteryEventPublisher(hubContext, db, logger);

        await publisher.PublishMasteryUpdatedAsync(mastery, 0.1f, CancellationToken.None);

        var sent = clients.CallerProxy.Sent;
        Assert.Contains(sent, s => s.Method == "MasteryUpdated");
        var payload = sent.Single(s => s.Method == "MasteryUpdated").Args[0]!;
        var payloadType = payload.GetType();
        Assert.Equal(string.Empty, payloadType.GetProperty("conceptName")!.GetValue(payload));
    }

    [Fact]
    public async Task PublishMasteryUpdatedAsync_SignalRThrows_SwallowsExceptionAndLogs()
    {
        var db = CreateDb();
        var concept = new Concept { Id = Guid.NewGuid(), Name = "Test", Description = "desc" };
        var mastery = new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = "user-789",
            ConceptId = concept.Id,
            MasteryProbability = 0.5f
        };
        db.Concepts.Add(concept);
        db.StudentMasteries.Add(mastery);
        await db.SaveChangesAsync();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SignalRMasteryEventPublisher>();

        var clients = new FakeHubClients();
        var hubContext = new FakeHubContextAccessor(clients);
        clients.CallerProxy.OnSendCoreAsync = async (method, args, ct) =>
        {
            throw new InvalidOperationException("SignalR transport failed");
        };

        var publisher = new SignalRMasteryEventPublisher(hubContext, db, logger);

        // Should not throw
        await publisher.PublishMasteryUpdatedAsync(mastery, 0.2f, CancellationToken.None);

        var sent = clients.CallerProxy.Sent;
        Assert.Contains(sent, s => s.Method == "MasteryUpdated");
    }
}