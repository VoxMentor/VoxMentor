using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Features.Practice.GetQuestions;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="GetQuestionsHandler"/> covering pagination,
/// concept/difficulty filters, and empty results.
/// </summary>
public class GetQuestionsHandlerTests
{
    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options);
    }

    private static async Task<Concept> SeedConceptAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string name = "Arrays")
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

    private static async Task<Question> SeedQuestionAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        Guid conceptId,
        string title = "Test Question",
        int difficulty = 3,
        int hiddenCount = 0)
    {
        var cases = new List<string>();
        for (var i = 0; i < 3; i++)
            cases.Add($"{{\"input\":\"{i}\",\"expected\":\"{i}\"}}");
        for (var i = 0; i < hiddenCount; i++)
            cases.Add($"{{\"input\":\"h{i}\",\"expected\":\"h{i}\",\"hidden\":true}}");

        var question = new Question
        {
            Id = Guid.NewGuid(),
            ConceptId = conceptId,
            Title = title,
            Description = "Desc",
            QuestionType = "Code",
            Difficulty = difficulty,
            TestCases = cases.ToArray(),
            HiddenTestCaseCount = hiddenCount
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        return question;
    }

    private static GetQuestionsHandler CreateHandler(Infrastructure.Persistence.ApplicationDbContext db)
        => new(db);

    [Fact]
    public async Task Handle_ReturnsQuestions_WithConceptName()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db, "DP");
        await SeedQuestionAsync(db, concept.Id, "Knapsack", 5);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetQuestionsQuery(), CancellationToken.None);

        Assert.True(response.Success);
        var q = Assert.Single(response.Data!.Questions);
        Assert.Equal("DP", q.ConceptName);
        Assert.Equal("Knapsack", q.Title);
        Assert.Equal(5, q.Difficulty);
    }

    [Fact]
    public async Task Handle_ConceptFilter_ReturnsOnlyMatching()
    {
        using var db = CreateDb();
        var c1 = await SeedConceptAsync(db, "Arrays");
        var c2 = await SeedConceptAsync(db, "DP");
        await SeedQuestionAsync(db, c1.Id, "Q1");
        await SeedQuestionAsync(db, c2.Id, "Q2");
        var handler = CreateHandler(db);

        var response = await handler.Handle(
            new GetQuestionsQuery(ConceptId: c1.Id), CancellationToken.None);

        Assert.Single(response.Data!.Questions);
        Assert.Equal("Q1", response.Data.Questions[0].Title);
    }

    [Fact]
    public async Task Handle_DifficultyFilter_ReturnsOnlyMatching()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        await SeedQuestionAsync(db, concept.Id, "Easy", difficulty: 2);
        await SeedQuestionAsync(db, concept.Id, "Hard", difficulty: 8);
        var handler = CreateHandler(db);

        var response = await handler.Handle(
            new GetQuestionsQuery(Difficulty: 2), CancellationToken.None);

        Assert.Single(response.Data!.Questions);
        Assert.Equal("Easy", response.Data.Questions[0].Title);
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPage()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        for (var i = 0; i < 5; i++)
            await SeedQuestionAsync(db, concept.Id, $"Q{i + 1}", difficulty: i + 1);
        var handler = CreateHandler(db);

        var page1 = await handler.Handle(new GetQuestionsQuery(Page: 1, PageSize: 2), CancellationToken.None);
        var page2 = await handler.Handle(new GetQuestionsQuery(Page: 2, PageSize: 2), CancellationToken.None);

        Assert.Equal(2, page1.Data!.Questions.Count);
        Assert.Equal(2, page2.Data!.Questions.Count);
        Assert.Equal(5, page1.Data.TotalCount);
        Assert.NotEqual(page1.Data.Questions[0].Id, page2.Data.Questions[0].Id);
    }

    [Fact]
    public async Task Handle_EmptyDb_ReturnsEmptyList()
    {
        using var db = CreateDb();
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetQuestionsQuery(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Empty(response.Data!.Questions);
        Assert.Equal(0, response.Data.TotalCount);
    }
}
