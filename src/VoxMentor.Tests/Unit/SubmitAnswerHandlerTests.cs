using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Practice.SubmitAnswer;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

public class SubmitAnswerHandlerTests
{
    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
    }

    private sealed class FakeEventPublisher : IMasteryEventPublisher
    {
        public int PublishedCount { get; private set; }

        public Task PublishMasteryUpdatedAsync(StudentMastery mastery, float previousMastery, CancellationToken cancellationToken = default)
        {
            PublishedCount++;
            return Task.CompletedTask;
        }
    }

    private static Infrastructure.Persistence.ApplicationDbContext CreateDb(string? sharedName = null)
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(sharedName ?? Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options);
    }

    private static async Task<Question> SeedQuestionAsync(Infrastructure.Persistence.ApplicationDbContext db, Guid? conceptId = null)
    {
        var question = new Question
        {
            Id = Guid.NewGuid(),
            ConceptId = conceptId ?? Guid.NewGuid(),
            Title = "Two Sum",
            Description = "Find two numbers that add up to target."
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        return question;
    }

    private static SubmitAnswerHandler CreateHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeCurrentUserService? user = null,
        FakeEventPublisher? publisher = null)
    {
        return new SubmitAnswerHandler(
            db,
            new BktEngine(),
            user ?? new FakeCurrentUserService(),
            publisher ?? new FakeEventPublisher());
    }

    [Fact]
    public async Task Handle_CorrectAnswer_CreatesMasteryAndIncreasesIt()
    {
        using var db = CreateDb();
        var question = await SeedQuestionAsync(db);
        var publisher = new FakeEventPublisher();
        var handler = CreateHandler(db, publisher: publisher);

        var response = await handler.Handle(new SubmitAnswerCommand(question.Id, true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(question.Id, response.Data!.QuestionId);
        Assert.True(response.Data.NewMastery > response.Data.PreviousMastery);
        Assert.True(response.Data.MasteryDelta > 0);
        Assert.Equal(1, response.Data.CorrectAttempts);
        Assert.Equal(0, response.Data.IncorrectAttempts);
        Assert.Equal(1, publisher.PublishedCount);

        var stored = await db.StudentMasteries.FirstAsync();
        Assert.Equal("user-1", stored.UserId);
        Assert.Equal(question.ConceptId, stored.ConceptId);
        Assert.NotNull(stored.LastPracticedAt);
    }

    [Fact]
    public async Task Handle_IncorrectAnswer_DecreasesExistingMastery()
    {
        using var db = CreateDb();
        var question = await SeedQuestionAsync(db);
        db.StudentMasteries.Add(new StudentMastery
        {
            UserId = "user-1",
            ConceptId = question.ConceptId,
            MasteryProbability = 0.8f,
            CorrectAttempts = 3
        });
        await db.SaveChangesAsync();
        var handler = CreateHandler(db);

        var response = await handler.Handle(new SubmitAnswerCommand(question.Id, false), CancellationToken.None);

        Assert.True(response.Data!.NewMastery < response.Data.PreviousMastery);
        Assert.Equal(3, response.Data.CorrectAttempts);
        Assert.Equal(1, response.Data.IncorrectAttempts);
    }

    [Fact]
    public async Task Handle_UnknownQuestion_ThrowsNotFound()
    {
        using var db = CreateDb();
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<Application.Common.Exceptions.NotFoundException>(
            () => handler.Handle(new SubmitAnswerCommand(Guid.NewGuid(), true), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UnauthenticatedUser_ThrowsUnauthorized()
    {
        using var db = CreateDb();
        var question = await SeedQuestionAsync(db);
        var handler = CreateHandler(db, user: new FakeCurrentUserService { UserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => handler.Handle(new SubmitAnswerCommand(question.Id, true), CancellationToken.None));
    }

    [Fact]
    public void Validator_EmptyQuestionId_Fails()
    {
        var validator = new SubmitAnswerValidator();
        var result = validator.Validate(new SubmitAnswerCommand(Guid.Empty, true));
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Handle_ConcurrentSubmissions_BothPersistWithoutLostUpdate()
    {
        var storeName = Guid.NewGuid().ToString();
        Guid questionId;
        using (var seedDb = CreateDb(storeName))
        {
            var question = await SeedQuestionAsync(seedDb);
            questionId = question.Id;
            seedDb.StudentMasteries.Add(new StudentMastery
            {
                UserId = "user-1",
                ConceptId = question.ConceptId,
                MasteryProbability = 0.5f
            });
            await seedDb.SaveChangesAsync();
        }

        using var db1 = CreateDb(storeName);
        using var db2 = CreateDb(storeName);
        var publisher1 = new FakeEventPublisher();
        var publisher2 = new FakeEventPublisher();

        var task1 = CreateHandler(db1, publisher: publisher1)
            .Handle(new SubmitAnswerCommand(questionId, true), CancellationToken.None);
        var task2 = CreateHandler(db2, publisher: publisher2)
            .Handle(new SubmitAnswerCommand(questionId, false), CancellationToken.None);
        var responses = await Task.WhenAll(task1, task2);

        Assert.All(responses, r => Assert.True(r.Success));

        using var verifyDb = CreateDb(storeName);
        var stored = await verifyDb.StudentMasteries.FirstAsync();
        Assert.Equal(1, stored.CorrectAttempts);
        Assert.Equal(1, stored.IncorrectAttempts);
        Assert.Equal(2, publisher1.PublishedCount + publisher2.PublishedCount);
    }
}
