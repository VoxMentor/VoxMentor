using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Admin.GetQuestions;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="GetQuestionsHandler"/> covering pagination tie-breaker
/// and integer overflow guard.
/// </summary>
public class GetQuestionsHandlerTests
{
    private static Infrastructure.Persistence.ApplicationDbContext CreateDb(string? name = null)
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options);
    }

    private static GetQuestionsHandler CreateHandler(Infrastructure.Persistence.ApplicationDbContext db)
        => new(db);

    private static async Task<Concept> SeedConceptAsync(Infrastructure.Persistence.ApplicationDbContext db)
    {
        var concept = new Concept
        {
            Id = Guid.NewGuid(),
            Name = "Arrays",
            Description = "Contiguous indexed storage.",
            DifficultyLevel = 2,
            Category = "Data Structures"
        };
        db.Concepts.Add(concept);
        await db.SaveChangesAsync();
        return concept;
    }

    private static async Task<Question> SeedQuestionAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        Guid conceptId,
        string title,
        DateTime createdAt)
    {
        var q = new Question
        {
            Id = Guid.NewGuid(),
            ConceptId = conceptId,
            Title = title,
            Description = "Desc",
            Difficulty = 2,
            TestCases = new[] { "{\"input\":\"1\",\"expected\":\"1\"}" },
            ExampleInputs = new[] { "1" },
            ExampleOutputs = new[] { "1" },
            StarterCode = Array.Empty<string>(),
            CreatedAt = createdAt
        };
        db.Questions.Add(q);
        await db.SaveChangesAsync();
        return q;
    }

    // ==================== Tie-Breaker Test ====================

    [Fact]
    public async Task Handle_SameCreatedAt_OrdersByIdForStability()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);

        // Two questions with identical CreatedAt — order must be deterministic by Id
        var fixedTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var q1 = await SeedQuestionAsync(db, concept.Id, "B-Question", fixedTime);
        var q2 = await SeedQuestionAsync(db, concept.Id, "A-Question", fixedTime);

        var handler = CreateHandler(db);
        var result = await handler.Handle(new GetQuestionsQuery(Page: 1, PageSize: 10), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.TotalCount);
        // Both should be present
        Assert.Contains(result.Data.Questions, q => q.Id == q1.Id);
        Assert.Contains(result.Data.Questions, q => q.Id == q2.Id);
        // Tie-breaker: same CreatedAt → ordered by Id ascending
        Assert.True(result.Data.Questions[0].Id.CompareTo(result.Data.Questions[1].Id) < 0,
            $"Expected Id {result.Data.Questions[0].Id} < {result.Data.Questions[1].Id}");
    }

    // ==================== Overflow Guard Test ====================

    [Fact]
    public async Task Handle_LargePageSize_ReturnsEmptyAndTotalCount()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        await SeedQuestionAsync(db, concept.Id, "Q1", DateTime.UtcNow);

        var handler = CreateHandler(db);
        // PageSize 100 with Page 100_000_000 → offset = 9_999_999_900 which exceeds int.MaxValue
        var result = await handler.Handle(new GetQuestionsQuery(Page: 100_000_000, PageSize: 100), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(result.Data!.Questions);
        Assert.Equal(1, result.Data.TotalCount);
        Assert.Equal(100_000_000, result.Data.Page);
        Assert.Equal(100, result.Data.PageSize);
    }
}
