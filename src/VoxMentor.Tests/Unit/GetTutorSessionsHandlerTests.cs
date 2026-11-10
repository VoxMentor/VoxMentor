using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Tutor.GetTutorSessions;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="GetTutorSessionsHandler"/> covering authentication,
/// empty profiles, user-scoped filtering, and newest-first ordering.
/// </summary>
public class GetTutorSessionsHandlerTests
{
    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
    }

    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options, new FakeCurrentUserService());
    }

    private static async Task<TutorSession> SeedSessionAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string userId,
        Guid? conceptId = null,
        string question = "What is a hash map?",
        TutorSessionStatus status = TutorSessionStatus.Completed,
        int totalTokens = 120,
        DateTime? createdAt = null,
        DateTime? completedAt = null)
    {
        var session = new TutorSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConceptId = conceptId,
            Question = question,
            Answer = "A hash map...",
            Status = status,
            TotalTokens = totalTokens,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            CompletedAt = completedAt,
        };
        db.TutorSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    [Fact]
    public async Task ReturnsEmptyList_WhenNoSessions()
    {
        await using var db = CreateDb();
        var handler = new GetTutorSessionsHandler(db, new FakeCurrentUserService());

        var response = await handler.Handle(new GetTutorSessionsQuery(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Empty(response.Data);
    }

    [Fact]
    public async Task ReturnsOnlyCurrentUsersSessions()
    {
        await using var db = CreateDb();
        await SeedSessionAsync(db, "user-1", question: "Own session 1", createdAt: DateTime.UtcNow.AddMinutes(-10));
        await SeedSessionAsync(db, "user-1", question: "Own session 2", createdAt: DateTime.UtcNow);
        await SeedSessionAsync(db, "other-user", question: "Other user session", createdAt: DateTime.UtcNow);

        var handler = new GetTutorSessionsHandler(db, new FakeCurrentUserService());

        var response = await handler.Handle(new GetTutorSessionsQuery(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Data.Count());
        Assert.All(response.Data, s => Assert.DoesNotContain("Other user session", s.Question));
        Assert.Contains(response.Data, s => s.Question == "Own session 1");
        Assert.Contains(response.Data, s => s.Question == "Own session 2");
    }

    [Fact]
    public async Task ReturnsNewestFirst()
    {
        await using var db = CreateDb();
        await SeedSessionAsync(db, "user-1", question: "Old session", createdAt: DateTime.UtcNow.AddDays(-2));
        await SeedSessionAsync(db, "user-1", question: "New session", createdAt: DateTime.UtcNow);
        await SeedSessionAsync(db, "user-1", question: "Middle session", createdAt: DateTime.UtcNow.AddDays(-1));

        var handler = new GetTutorSessionsHandler(db, new FakeCurrentUserService());

        var response = await handler.Handle(new GetTutorSessionsQuery(), CancellationToken.None);

        Assert.True(response.Success);
        var sessions = response.Data.ToList();
        Assert.Equal(3, sessions.Count);
        Assert.Equal("New session", sessions[0].Question);
        Assert.Equal("Middle session", sessions[1].Question);
        Assert.Equal("Old session", sessions[2].Question);
    }

    [Fact]
    public async Task Throws_WhenUnauthenticated()
    {
        await using var db = CreateDb();
        var handler = new GetTutorSessionsHandler(db, new FakeCurrentUserService { UserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => handler.Handle(new GetTutorSessionsQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task RespectsLimit()
    {
        await using var db = CreateDb();
        for (int i = 0; i < 5; i++)
        {
            await SeedSessionAsync(db, "user-1", question: $"Session {i}", createdAt: DateTime.UtcNow.AddMinutes(-i));
        }

        var handler = new GetTutorSessionsHandler(db, new FakeCurrentUserService());

        var response = await handler.Handle(new GetTutorSessionsQuery { Limit = 2 }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Data.Count());
    }
}
