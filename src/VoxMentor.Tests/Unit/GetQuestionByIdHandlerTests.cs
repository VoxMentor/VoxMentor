using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Features.Practice.GetQuestionById;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="GetQuestionByIdHandler"/> covering happy path,
/// hidden test case stripping, and not-found cases.
/// </summary>
public class GetQuestionByIdHandlerTests
{
    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options);
    }

    private static async Task<(Concept concept, Question question)> SeedQuestionWithConceptAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        int hiddenCount = 0)
    {
        var concept = new Concept
        {
            Id = Guid.NewGuid(),
            Name = "Arrays",
            Description = "Arrays desc",
            DifficultyLevel = 2,
            Category = "Data Structures"
        };
        db.Concepts.Add(concept);

        var cases = new[]
        {
            "{\"input\":\"1 2\",\"expected\":\"3\"}",
            "{\"input\":\"3 4\",\"expected\":\"7\"}",
            "{\"input\":\"h1\",\"expected\":\"h2\",\"hidden\":true}"
        };

        var question = new Question
        {
            Id = Guid.NewGuid(),
            ConceptId = concept.Id,
            Title = "Two Sum",
            Description = "Find two numbers",
            QuestionType = "Code",
            Difficulty = 3,
            TestCases = cases,
            ExampleInputs = new[] { "[1,2]" },
            ExampleOutputs = new[] { "3" },
            StarterCode = new[] { "def solve(): pass" },
            Rubric = new[] { "{\"criterion\":\"Correctness\",\"points\":5}" },
            HiddenTestCaseCount = hiddenCount
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        return (concept, question);
    }

    private static GetQuestionByIdHandler CreateHandler(Infrastructure.Persistence.ApplicationDbContext db)
        => new(db);

    [Fact]
    public async Task Handle_ValidId_ReturnsQuestionWithConceptName()
    {
        using var db = CreateDb();
        var (_, question) = await SeedQuestionWithConceptAsync(db, hiddenCount: 1);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetQuestionByIdQuery(question.Id), CancellationToken.None);

        Assert.True(response.Success);
        var dto = response.Data!;
        Assert.Equal("Two Sum", dto.Title);
        Assert.Equal("Arrays", dto.ConceptName);
        Assert.Equal("Code", dto.QuestionType);
        Assert.Equal(3, dto.TotalTestCases);
        Assert.Equal(1, dto.HiddenTestCaseCount);
        Assert.Equal(2, dto.VisibleTestCases.Length);
        Assert.Single(dto.ExampleInputs);
        Assert.Single(dto.StarterCode);
        Assert.Single(dto.Rubric);
    }

    [Fact]
    public async Task Handle_NoHiddenCases_ReturnsAllTestCases()
    {
        using var db = CreateDb();
        var (_, question) = await SeedQuestionWithConceptAsync(db, hiddenCount: 0);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetQuestionByIdQuery(question.Id), CancellationToken.None);

        Assert.Equal(3, response.Data!.VisibleTestCases.Length);
        Assert.Equal(0, response.Data.HiddenTestCaseCount);
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsNotFoundException()
    {
        using var db = CreateDb();
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new GetQuestionByIdQuery(Guid.NewGuid()), CancellationToken.None));
    }
}
