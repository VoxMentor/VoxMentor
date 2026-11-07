using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Linq;
using System.Threading;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Jd.GetJd;
using VoxMentor.Application.Features.Practice.GetMastery;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="GetMasteryHandler"/> covering authentication,
/// empty profiles, mastered thresholds, and readiness aggregation.
/// </summary>
public class GetMasteryHandlerTests
{
    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
    }

    /// <summary>Creates an InMemory database context.</summary>
    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options, new FakeCurrentUserService());
    }

    /// <summary>Seeds a concept with a deterministic unique id.</summary>
    private static async Task<Concept> SeedConceptAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string name = "Arrays",
        string category = "Data Structures",
        int difficulty = 2)
    {
        var concept = new Concept
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = $"{name} concept",
            DifficultyLevel = difficulty,
            Category = category
        };
        db.Concepts.Add(concept);
        await db.SaveChangesAsync();
        return concept;
    }

    /// <summary>Seeds a mastery row for the given user and concept.</summary>
    private static async Task SeedMasteryAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string userId,
        Guid conceptId,
        float masteryProbability,
        int correct = 3,
        int incorrect = 1)
    {
        db.StudentMasteries.Add(new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConceptId = conceptId,
            MasteryProbability = masteryProbability,
            CorrectAttempts = correct,
            IncorrectAttempts = incorrect,
            LastPracticedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds a JD for the given user.</summary>
    private static async Task<JobDescription> SeedJdAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string userId,
        int estimatedWeeks = 4)
    {
        var jd = new JobDescription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompanyName = "Amazon",
            Role = "SDE-1",
            RawText = "Amazon is hiring...",
            Difficulty = "Medium",
            EstimatedWeeks = estimatedWeeks,
            CreatedAt = DateTime.UtcNow
        };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();
        return jd;
    }

    /// <summary>Builds a handler over the given context with an optional current-user fake.</summary>
    private static GetMasteryHandler CreateHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeCurrentUserService? user = null)
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        return new GetMasteryHandler(db, user ?? new FakeCurrentUserService(), cache);
    }

    /// <summary>Verifies that an unauthenticated request is rejected with UnauthorizedAccessException.</summary>
    [Fact]
    public async Task Handle_UnauthenticatedUser_Throws()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db, new FakeCurrentUserService { UserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new GetMasteryQuery(), CancellationToken.None));
    }

    /// <summary>Verifies an empty knowledge graph yields an all-zero empty profile.</summary>
    [Fact]
    public async Task Handle_NoConcepts_ReturnsEmptyProfile()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetMasteryQuery(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Empty(response.Data.Concepts);
        Assert.Equal(0, response.Data.TotalConcepts);
        Assert.Equal(0, response.Data.MasteredCount);
        Assert.Equal(0f, response.Data.OverallReadiness);
    }

    /// <summary>Verifies never-practiced concepts report null mastery, zero attempts, and zero readiness.</summary>
    [Fact]
    public async Task Handle_NeverPracticed_ReportsNullMasteryAndZeroReadiness()
    {
        await using var db = CreateDb();
        await SeedConceptAsync(db, "Variables");
        await SeedConceptAsync(db, "Arrays");
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetMasteryQuery(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Data.TotalConcepts);
        Assert.All(response.Data.Concepts, c =>
        {
            Assert.Null(c.MasteryProbability);
            Assert.False(c.IsMastered);
            Assert.Equal(0, c.CorrectAttempts);
            Assert.Equal(0, c.IncorrectAttempts);
            Assert.Null(c.LastPracticedAt);
        });
        Assert.Equal(0, response.Data.MasteredCount);
        Assert.Equal(0f, response.Data.OverallReadiness);
    }

    /// <summary>Verifies a concept at 0.9 mastery counts as mastered and lifts readiness.</summary>
    [Fact]
    public async Task Handle_MasteredConcept_CountsAsMastered()
    {
        await using var db = CreateDb();
        var concept = await SeedConceptAsync(db, "Recursion");
        await SeedMasteryAsync(db, "user-1", concept.Id, masteryProbability: 0.9f);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetMasteryQuery(), CancellationToken.None);

        var row = Assert.Single(response.Data.Concepts);
        Assert.Equal(0.9f, row.MasteryProbability);
        Assert.True(row.IsMastered);
        Assert.Equal(1, response.Data.MasteredCount);
        Assert.Equal(1, response.Data.TotalConcepts);
        Assert.Equal(0.9f, response.Data.OverallReadiness);
    }

    /// <summary>Verifies mastery just below the 0.85 threshold (0.84) is not mastered.</summary>
    [Fact]
    public async Task Handle_MasteryBelowThreshold_NotMastered()
    {
        await using var db = CreateDb();
        var concept = await SeedConceptAsync(db, "Recursion");
        await SeedMasteryAsync(db, "user-1", concept.Id, masteryProbability: 0.84f);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetMasteryQuery(), CancellationToken.None);

        var row = Assert.Single(response.Data.Concepts);
        Assert.Equal(0.84f, row.MasteryProbability);
        Assert.False(row.IsMastered);
        Assert.Equal(0, response.Data.MasteredCount);
    }

    /// <summary>Verifies the inclusive 0.85 threshold boundary counts as mastered.</summary>
    [Fact]
    public async Task Handle_ThresholdExactlyAtLimit_CountsAsMastered()
    {
        await using var db = CreateDb();
        var concept = await SeedConceptAsync(db, "Recursion");
        await SeedMasteryAsync(db, "user-1", concept.Id, masteryProbability: 0.85f);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetMasteryQuery(), CancellationToken.None);

        Assert.True(response.Data.Concepts.Single().IsMastered);
        Assert.Equal(1, response.Data.MasteredCount);
    }

    /// <summary>Verifies readiness averages all concepts, counting unpracticed as zero.</summary>
    [Fact]
    public async Task Handle_MixedProfile_ComputesReadinessWithUnpracticedAsZero()
    {
        await using var db = CreateDb();
        var mastered = await SeedConceptAsync(db, "Recursion");
        var partial = await SeedConceptAsync(db, "Arrays");
        await SeedConceptAsync(db, "Variables");
        await SeedMasteryAsync(db, "user-1", mastered.Id, masteryProbability: 0.9f);
        await SeedMasteryAsync(db, "user-1", partial.Id, masteryProbability: 0.4f);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetMasteryQuery(), CancellationToken.None);

        Assert.Equal(3, response.Data.TotalConcepts);
        Assert.Equal(1, response.Data.MasteredCount);
        Assert.Equal((0.9f + 0.4f + 0f) / 3f, response.Data.OverallReadiness);
    }

    /// <summary>Verifies other users' mastery rows never leak into the caller's profile.</summary>
    [Fact]
    public async Task Handle_OtherUsersMastery_ExcludedFromProfile()
    {
        await using var db = CreateDb();
        var concept = await SeedConceptAsync(db, "Recursion");
        await SeedMasteryAsync(db, "user-2", concept.Id, masteryProbability: 1.0f);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetMasteryQuery(), CancellationToken.None);

        var row = Assert.Single(response.Data.Concepts);
        Assert.Null(row.MasteryProbability);
        Assert.Equal(0, response.Data.MasteredCount);
        Assert.Equal(0f, response.Data.OverallReadiness);
    }

    /// <summary>Verifies concepts are ordered by category, then difficulty, then name.</summary>
    [Fact]
    public async Task Handle_ConceptsOrdered_ByCategoryThenDifficultyThenName()
    {
        await using var db = CreateDb();
        await SeedConceptAsync(db, "B-Second", category: "Sorting", difficulty: 1);
        await SeedConceptAsync(db, "A-First", category: "Fundamentals", difficulty: 1);
        await SeedConceptAsync(db, "C-Harder", category: "Fundamentals", difficulty: 3);
        await SeedConceptAsync(db, "D-Easier", category: "Fundamentals", difficulty: 1);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetMasteryQuery(), CancellationToken.None);

        var names = response.Data.Concepts.Select(c => c.Name).ToArray();
        Assert.Equal(new[] { "A-First", "D-Easier", "C-Harder", "B-Second" }, names);
    }

    /// <summary>Verifies a second call for the same user returns the cached profile
    /// without hitting the database again.</summary>
    [Fact]
    public async Task Handle_RepeatedRequest_ReturnsCachedProfileWithoutDbQuery()
    {
        await using var db = CreateDb();
        var concept = await SeedConceptAsync(db, "Recursion");
        await SeedMasteryAsync(db, "user-1", concept.Id, masteryProbability: 0.9f);

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var handler = new GetMasteryHandler(db, new FakeCurrentUserService(), cache);

        // First call populates the cache.
        var first = await handler.Handle(new GetMasteryQuery(), CancellationToken.None);
        Assert.True(first.Success);
        Assert.Equal(1, first.Data!.MasteredCount);

        // Change the database so a non-cached second call would return different data.
        var updated = await db.StudentMasteries.FirstAsync();
        updated.MasteryProbability = 0.1f;
        await db.SaveChangesAsync();

        // Second call must return the cached (stale) profile, not the updated DB data.
        var second = await handler.Handle(new GetMasteryQuery(), CancellationToken.None);
        Assert.True(second.Success);
        Assert.Equal(1, second.Data.MasteredCount);
        Assert.Equal(0.9f, second.Data.Concepts.Single().MasteryProbability);
    }

    /// <summary>Cache that throws on every Get and Set, simulating Redis unavailable.</summary>
    private sealed class FailingCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Redis down");

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("Redis down");

        public void Refresh(string key) => throw new InvalidOperationException("Redis down");

        public Task RefreshAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("Redis down");

        public void Remove(string key) => throw new InvalidOperationException("Redis down");

        public Task RemoveAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("Redis down");

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new InvalidOperationException("Redis down");

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            throw new InvalidOperationException("Redis down");
    }

    /// <summary>Verifies GetMasteryHandler returns results from the database
    /// when the cache throws on both Get and Set (Redis unavailable fallback).</summary>
    [Fact]
    public async Task Handle_CacheUnavailable_ReturnsDatabaseResultWithoutThrowing()
    {
        await using var db = CreateDb();
        var concept = await SeedConceptAsync(db, "Recursion");
        await SeedMasteryAsync(db, "user-1", concept.Id, masteryProbability: 0.9f);

        var handler = new GetMasteryHandler(db, new FakeCurrentUserService(), new FailingCache());

        var response = await handler.Handle(new GetMasteryQuery(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(1, response.Data!.MasteredCount);
        Assert.Equal(0.9f, response.Data.Concepts.Single().MasteryProbability);
    }

    /// <summary>Verifies GetJdHandler returns results from the database
    /// when the cache throws on both Get and Set (Redis unavailable fallback).</summary>
    [Fact]
    public async Task GetJdHandler_CacheUnavailable_ReturnsDatabaseResultWithoutThrowing()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        var handler = new GetJdHandler(db, new FakeCurrentUserService(), new FailingCache());

        var response = await handler.Handle(new GetJdQuery(jd.Id), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(jd.Id, response.Data!.JdId);
    }
}
