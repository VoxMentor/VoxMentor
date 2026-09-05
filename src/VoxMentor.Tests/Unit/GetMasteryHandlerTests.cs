using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
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
        return new Infrastructure.Persistence.ApplicationDbContext(options);
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

    /// <summary>Builds a handler over the given context with an optional current-user fake.</summary>
    private static GetMasteryHandler CreateHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeCurrentUserService? user = null)
    {
        return new GetMasteryHandler(db, user ?? new FakeCurrentUserService());
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
}
