using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Practice.GetNextQuestion;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="GetNextQuestionHandler"/> covering weakest-concept
/// selection, difficulty formula, already-attempted exclusion, and edge cases.
/// </summary>
public class GetNextQuestionHandlerTests
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
        return new Infrastructure.Persistence.ApplicationDbContext(options);
    }

    private static async Task<Concept> SeedConceptAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string name = "Arrays",
        int difficulty = 2)
    {
        var concept = new Concept
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = $"{name} desc",
            DifficultyLevel = difficulty,
            Category = "Data Structures"
        };
        db.Concepts.Add(concept);
        await db.SaveChangesAsync();
        return concept;
    }

    private static async Task<Question> SeedQuestionAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        Guid conceptId,
        int difficulty = 3,
        string title = "Q1")
    {
        var question = new Question
        {
            Id = Guid.NewGuid(),
            ConceptId = conceptId,
            Title = title,
            Description = "Desc",
            QuestionType = "Code",
            Difficulty = difficulty,
            TestCases = new[]
            {
                "{\"input\":\"1\",\"expected\":\"1\"}",
                "{\"input\":\"2\",\"expected\":\"2\",\"hidden\":true}"
            },
            HiddenTestCaseCount = 1
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        return question;
    }

    private static async Task SeedMasteryAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string userId,
        Guid conceptId,
        float mastery)
    {
        db.StudentMasteries.Add(new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConceptId = conceptId,
            MasteryProbability = mastery,
            CorrectAttempts = (int)(mastery * 10),
            IncorrectAttempts = (int)((1 - mastery) * 5)
        });
        await db.SaveChangesAsync();
    }

    private static GetNextQuestionHandler CreateHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeCurrentUserService? user = null)
        => new(db, user ?? new FakeCurrentUserService());

    [Fact]
    public async Task Handle_NoMastery_PicksWeakestConcept()
    {
        using var db = CreateDb();
        var c1 = await SeedConceptAsync(db, "Arrays");
        var c2 = await SeedConceptAsync(db, "DP");
        await SeedQuestionAsync(db, c1.Id, difficulty: 3, title: "ArrayQ");
        await SeedQuestionAsync(db, c2.Id, difficulty: 5, title: "DPQ");
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetNextQuestionQuery(), CancellationToken.None);

        Assert.True(response.Success);
        // Both unpracticed → same mastery (0.1), sorted by name → Arrays first
        Assert.Equal("Arrays", response.Data!.ConceptName);
        Assert.Equal("ArrayQ", response.Data.Title);
        // Target difficulty = 1 + 0.1*9 = 1.9 ≈ 2, closest is 3
        Assert.Equal(3, response.Data.Difficulty);
    }

    [Fact]
    public async Task Handle_WithMastery_TargetsWeakestConcept()
    {
        using var db = CreateDb();
        var c1 = await SeedConceptAsync(db, "Arrays");
        var c2 = await SeedConceptAsync(db, "DP");
        await SeedMasteryAsync(db, "user-1", c1.Id, 0.9f);
        await SeedMasteryAsync(db, "user-1", c2.Id, 0.2f);
        await SeedQuestionAsync(db, c1.Id, difficulty: 9, title: "ArrayQ");
        await SeedQuestionAsync(db, c2.Id, difficulty: 3, title: "DPQ");
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetNextQuestionQuery(), CancellationToken.None);

        Assert.Equal("DP", response.Data!.ConceptName);
        Assert.Equal("DPQ", response.Data.Title);
    }

    [Fact]
    public async Task Handle_DifficultyFormula_Correct()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db, "DP");
        await SeedMasteryAsync(db, "user-1", concept.Id, 0.5f);
        // mastery=0.5 → target = 1 + 0.5*9 = 5.5 ≈ 6
        await SeedQuestionAsync(db, concept.Id, difficulty: 6, title: "Target");
        await SeedQuestionAsync(db, concept.Id, difficulty: 2, title: "Far");
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetNextQuestionQuery(), CancellationToken.None);

        Assert.Equal("Target", response.Data!.Title);
    }

    [Fact]
    public async Task Handle_ExcludesAlreadyAttempted()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db, "Arrays");
        var q1 = await SeedQuestionAsync(db, concept.Id, difficulty: 3, title: "Done");
        await SeedQuestionAsync(db, concept.Id, difficulty: 3, title: "Fresh");
        // Mark q1 as attempted
        db.CodeSubmissions.Add(new CodeSubmission
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            QuestionId = q1.Id,
            Code = "x",
            Language = "python",
            IsCorrect = true,
            TestCasesPassed = 1,
            TestCasesTotal = 1,
            Status = SubmissionStatus.Accepted
        });
        await db.SaveChangesAsync();
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetNextQuestionQuery(), CancellationToken.None);

        Assert.Equal("Fresh", response.Data!.Title);
    }

    [Fact]
    public async Task Handle_AllAttempted_FallsBackToAnyUnanswered()
    {
        using var db = CreateDb();
        var c1 = await SeedConceptAsync(db, "Arrays");
        var c2 = await SeedConceptAsync(db, "DP");
        var q1 = await SeedQuestionAsync(db, c1.Id, difficulty: 3, title: "ArrayDone");
        var q2 = await SeedQuestionAsync(db, c2.Id, difficulty: 5, title: "DPFresh");
        // Attempt all concept-1 questions
        db.CodeSubmissions.Add(new CodeSubmission
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            QuestionId = q1.Id,
            Code = "x",
            Language = "python",
            IsCorrect = true,
            TestCasesPassed = 1,
            TestCasesTotal = 1,
            Status = SubmissionStatus.Accepted
        });
        await db.SaveChangesAsync();
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetNextQuestionQuery(), CancellationToken.None);

        // Should fall back to DP question
        Assert.Equal("DPFresh", response.Data!.Title);
    }

    [Fact]
    public async Task Handle_NoQuestions_ThrowsNotFoundException()
    {
        using var db = CreateDb();
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new GetNextQuestionQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Unauthenticated_ThrowsUnauthorized()
    {
        using var db = CreateDb();
        var handler = CreateHandler(db, new FakeCurrentUserService { UserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => handler.Handle(new GetNextQuestionQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_VisibleTestCases_ExcludesHidden()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        await SeedQuestionAsync(db, concept.Id);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetNextQuestionQuery(), CancellationToken.None);

        Assert.Single(response.Data!.VisibleTestCases);
        Assert.Equal(2, response.Data.VisibleTestCases.Length + 1); // 1 visible + 1 hidden = 2 total
    }
}
