using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Practice.GetSubmissions;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="GetSubmissionsHandler"/> covering ordering,
/// limit clamping, mastery delta computation, and joins to question/concept.
/// </summary>
public class GetSubmissionsHandlerTests
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

    private static async Task<(Concept concept, Question question)> SeedQuestionAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string conceptName,
        string questionTitle)
    {
        var concept = new Concept
        {
            Id = Guid.NewGuid(),
            Name = conceptName,
            Description = $"{conceptName} desc",
            DifficultyLevel = 2,
            Category = "Data Structures"
        };
        var question = new Question
        {
            Id = Guid.NewGuid(),
            ConceptId = concept.Id,
            Title = questionTitle,
            Description = "Desc",
            QuestionType = "Code",
            Difficulty = 3,
            TestCases = new[] { "{\"input\":\"1\",\"expected\":\"1\"}" },
            HiddenTestCaseCount = 0
        };
        db.Concepts.Add(concept);
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        return (concept, question);
    }

    private static async Task<CodeSubmission> SeedSubmissionAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        Guid questionId,
        DateTime createdAt,
        bool isCorrect,
        float? before = null,
        float? after = null,
        string userId = "user-1")
    {
        var submission = new CodeSubmission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            QuestionId = questionId,
            Code = "x",
            Language = "python",
            IsCorrect = isCorrect,
            TestCasesPassed = isCorrect ? 1 : 0,
            TestCasesTotal = 1,
            Status = isCorrect ? SubmissionStatus.Accepted : SubmissionStatus.WrongAnswer,
            CreatedAt = createdAt,
            MasteryBefore = before,
            MasteryAfter = after
        };
        db.CodeSubmissions.Add(submission);
        await db.SaveChangesAsync();
        return submission;
    }

    private static GetSubmissionsHandler CreateHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeCurrentUserService? user = null)
        => new(db, user ?? new FakeCurrentUserService());

    [Fact]
    public async Task Handle_ReturnsNewestFirst_WithJoinsAndDelta()
    {
        using var db = CreateDb();
        var (_, q) = await SeedQuestionAsync(db, "Arrays", "Two Sum");
        await SeedSubmissionAsync(db, q.Id, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), isCorrect: false, before: 0.4f, after: 0.35f);
        await SeedSubmissionAsync(db, q.Id, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), isCorrect: true, before: 0.35f, after: 0.5f);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetSubmissionsQuery(), CancellationToken.None);

        Assert.True(response.Success);
        var items = response.Data!;
        Assert.Equal(2, items.Count);
        // Newest first
        Assert.True(items[0].CreatedAt > items[1].CreatedAt);
        Assert.True(items[0].IsCorrect);
        Assert.Equal("Two Sum", items[0].QuestionTitle);
        Assert.Equal("Arrays", items[0].ConceptName);
        Assert.Equal(0.15f, items[0].MasteryDelta!.Value, 3);
        Assert.Equal(-0.05f, items[1].MasteryDelta!.Value, 3);
    }

    [Fact]
    public async Task Handle_RespectsLimit()
    {
        using var db = CreateDb();
        var (_, q) = await SeedQuestionAsync(db, "Arrays", "Q");
        for (var i = 0; i < 5; i++)
        {
            await SeedSubmissionAsync(db, q.Id, new DateTime(2026, 1, 1 + i, 0, 0, 0, DateTimeKind.Utc), isCorrect: true);
        }
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetSubmissionsQuery { Limit = 2 }, CancellationToken.None);

        Assert.Equal(2, response.Data!.Count);
        // Newest two: Jan 5 and Jan 4
        Assert.Equal(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), response.Data[0].CreatedAt);
        Assert.Equal(new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc), response.Data[1].CreatedAt);
    }

    [Fact]
    public async Task Handle_OnlyOwnSubmissions()
    {
        using var db = CreateDb();
        var (_, q) = await SeedQuestionAsync(db, "Arrays", "Q");
        await SeedSubmissionAsync(db, q.Id, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), isCorrect: true, userId: "other-user");
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetSubmissionsQuery(), CancellationToken.None);

        Assert.Empty(response.Data!);
    }

    [Fact]
    public async Task Handle_NullMasterySnapshots_YieldsNullDelta()
    {
        using var db = CreateDb();
        var (_, q) = await SeedQuestionAsync(db, "Arrays", "Q");
        await SeedSubmissionAsync(db, q.Id, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), isCorrect: false);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetSubmissionsQuery(), CancellationToken.None);

        Assert.Null(response.Data![0].MasteryDelta);
    }

    [Fact]
    public async Task Handle_Limit_DefaultsTo10_AndClampsTo1To50()
    {
        using var db = CreateDb();
        var (_, q) = await SeedQuestionAsync(db, "Arrays", "Q");
        for (var i = 0; i < 55; i++)
        {
            await SeedSubmissionAsync(db, q.Id, new DateTime(2026, 1, 1, 0, 0, i, DateTimeKind.Utc), isCorrect: true);
        }
        var handler = CreateHandler(db);

        var defaultResponse = await handler.Handle(new GetSubmissionsQuery(), CancellationToken.None);
        Assert.Equal(10, defaultResponse.Data!.Count);

        var zeroResponse = await handler.Handle(new GetSubmissionsQuery { Limit = 0 }, CancellationToken.None);
        Assert.Single(zeroResponse.Data!);

        var hugeResponse = await handler.Handle(new GetSubmissionsQuery { Limit = 500 }, CancellationToken.None);
        Assert.Equal(50, hugeResponse.Data!.Count);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ThrowsUnauthorized()
    {
        using var db = CreateDb();
        var handler = CreateHandler(db, new FakeCurrentUserService { UserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => handler.Handle(new GetSubmissionsQuery(), CancellationToken.None));
    }
}
