using System.Runtime.CompilerServices;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.MockInterviews;
using VoxMentor.Application.Features.Practice.GetReadiness;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="CompleteMockInterviewHandler"/>: 404/409 guards,
/// persisted Score/ReviewJson/CompletedAt, canned fallback (no mastery writes),
/// BKT updates from the AI verdicts, and the readiness-delta ordering (#125).
/// </summary>
public class CompleteMockInterviewHandlerTests
{
    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
    }

    private sealed class StubGenerator : IMockInterviewGenerator
    {
        public MockInterviewReviewDraft? Review { get; set; }

        public async IAsyncEnumerable<TutorChunk> OpeningStreamAsync(
            string? company, string? role, string problemTitle,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<TutorChunk> FollowUpStreamAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<bool?> AssessAnswerSolvedAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            InterviewType type,
            CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(false);

        public Task<MockInterviewReviewDraft?> ReviewAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            InterviewType type,
            string? company,
            string? role,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Review);
    }

    private sealed record TestApp(
        ServiceProvider Provider,
        ISender Sender,
        ApplicationDbContext Db,
        StubGenerator Generator) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private static TestApp CreateApp(string userId = "user-1")
    {
        var generator = new StubGenerator();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();
        services.AddScoped<IApplicationDbContext>(_ =>
            new ApplicationDbContext(options, new FakeCurrentUserService { UserId = userId }));
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService { UserId = userId });
        services.AddSingleton<IMockInterviewGenerator>(generator);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // The SignalR publisher lives in Program.cs; unit tests take the no-op.
        services.AddLogging();
        services.AddScoped<IMasteryEventPublisher, NullMasteryEventPublisher>();
        services.AddApplicationServices();

        var provider = services.BuildServiceProvider();
        return new TestApp(provider, provider.GetRequiredService<ISender>(), new ApplicationDbContext(options, new FakeCurrentUserService { UserId = userId }), generator);
    }

    private static async Task<Concept> SeedConceptAsync(ApplicationDbContext db, string name = "Arrays")
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

    private static async Task<Question> SeedQuestionAsync(ApplicationDbContext db, Guid conceptId, string title = "Max Subarray Sum")
    {
        var question = new Question
        {
            Id = Guid.NewGuid(),
            ConceptId = conceptId,
            Title = title,
            Description = "Desc",
            QuestionType = "Code",
            Difficulty = 3,
            TestCases = new[] { "{\"input\":\"1\",\"expected\":\"1\"}" },
            HiddenTestCaseCount = 0
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        return question;
    }

    private static async Task<Guid> SeedJdAsync(ApplicationDbContext db, string userId, string company = "Acme", string role = "SDE-1")
    {
        var jd = new JobDescription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompanyName = company,
            Role = role,
            RawText = "Acme is hiring...",
            Difficulty = "Medium",
            EstimatedWeeks = 6
        };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();
        return jd.Id;
    }

    private static async Task SeedSkillWeightAsync(ApplicationDbContext db, Guid jdId, string skillName, float weight)
    {
        db.JdSkillWeights.Add(new JdSkillWeight
        {
            Id = Guid.NewGuid(),
            JobDescriptionId = jdId,
            SkillName = skillName,
            Weight = weight,
            IsTechnical = true
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedMasteryAsync(ApplicationDbContext db, string userId, Guid conceptId, float probability)
    {
        db.StudentMasteries.Add(new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConceptId = conceptId,
            MasteryProbability = probability
        });
        await db.SaveChangesAsync();
    }

    private static async Task<MockInterview> SeedInterviewAsync(
        ApplicationDbContext db,
        string userId = "user-1",
        MockInterviewStatus status = MockInterviewStatus.InProgress,
        Guid? jdId = null,
        Guid? questionId = null)
    {
        var turns = new List<TranscriptTurn>
        {
            new(TranscriptTurn.InterviewerRole, "Opening problem.", questionId, DateTime.UtcNow),
            new(TranscriptTurn.CandidateRole, "My answer.", null, DateTime.UtcNow)
        };
        var interview = new MockInterview
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = InterviewType.Technical,
            TotalQuestions = 3,
            AnsweredQuestions = 1,
            Status = status,
            StartedAt = DateTime.UtcNow,
            JdId = jdId,
            TranscriptJson = System.Text.Json.JsonSerializer.Serialize(turns, TranscriptTurn.JsonOptions)
        };
        db.MockInterviews.Add(interview);
        await db.SaveChangesAsync();
        return interview;
    }

    private static MockInterviewReviewDraft Draft(bool solved = true) => new(
        80f,
        new Dictionary<string, ProblemReview>(StringComparer.Ordinal)
        {
            ["problem1"] = new ProblemReview(solved, "Optimal", "Clear approach with edge cases covered.")
        },
        "Practice narrating complexity while you code.");

    [Fact]
    public async Task Complete_UnknownInterview_ThrowsNotFound()
    {
        using var app = CreateApp();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            app.Sender.Send(new CompleteMockInterviewCommand { InterviewId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Complete_OtherStudentsInterview_ThrowsNotFound()
    {
        using var app = CreateApp();
        var foreign = await SeedInterviewAsync(app.Db, userId: "user-2");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            app.Sender.Send(new CompleteMockInterviewCommand { InterviewId = foreign.Id }));
    }

    [Theory]
    [InlineData(MockInterviewStatus.Completed)]
    [InlineData(MockInterviewStatus.Failed)]
    public async Task Complete_AlreadyEndedInterview_ThrowsConflict(MockInterviewStatus status)
    {
        using var app = CreateApp();
        var interview = await SeedInterviewAsync(app.Db, status: status);

        await Assert.ThrowsAsync<ConflictException>(() =>
            app.Sender.Send(new CompleteMockInterviewCommand { InterviewId = interview.Id }));
    }

    [Fact]
    public async Task Complete_GeneratorUnavailable_PersistsCannedReview_DeltaZero_NoMasteryWrites()
    {
        using var app = CreateApp();
        var concept = await SeedConceptAsync(app.Db);
        var question = await SeedQuestionAsync(app.Db, concept.Id);
        var interview = await SeedInterviewAsync(app.Db, questionId: question.Id);
        app.Generator.Review = null;

        var result = await app.Sender.Send(new CompleteMockInterviewCommand { InterviewId = interview.Id });

        // The handler saves through its own scoped context; drop our tracked
        // copy so the verification reads hit the shared store, not the cache.
        app.Db.ChangeTracker.Clear();

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(50f, result.Data!.Score);
        Assert.Equal("Technical", result.Data.Type);
        Assert.Equal("problem1", Assert.Single(result.Data.TechnicalReview).Key);
        Assert.Equal(result.Data.ReadinessDelta.Before, result.Data.ReadinessDelta.After);
        Assert.Equal(0f, result.Data.ReadinessDelta.Delta);
        Assert.False(result.Data.TechnicalReview["problem1"].Solved);

        var stored = await app.Db.MockInterviews.IgnoreQueryFilters().SingleAsync(m => m.Id == interview.Id);
        Assert.Equal(MockInterviewStatus.Completed, stored.Status);
        Assert.NotNull(stored.CompletedAt);
        Assert.Equal(50f, stored.Score);

        var review = System.Text.Json.JsonSerializer.Deserialize<MockInterviewReview>(
            stored.ReviewJson!, TranscriptTurn.JsonOptions);
        Assert.NotNull(review);
        Assert.Equal(result.Data.NextSteps, review!.NextSteps);
        Assert.Equal(result.Data.ReadinessDelta.Before, review.ReadinessDelta.Before);

        // Degraded path must not fabricate isCorrect=false mastery writes.
        Assert.Empty(await app.Db.StudentMasteries.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Complete_GeneratorAvailable_PersistsReview_AndAppliesBkt()
    {
        using var app = CreateApp();
        var concept = await SeedConceptAsync(app.Db);
        var question = await SeedQuestionAsync(app.Db, concept.Id);
        var interview = await SeedInterviewAsync(app.Db, questionId: question.Id);
        app.Generator.Review = Draft(solved: true);

        var result = await app.Sender.Send(new CompleteMockInterviewCommand { InterviewId = interview.Id });

        app.Db.ChangeTracker.Clear();

        Assert.True(result.Success);
        Assert.Equal(80f, result.Data!.Score);
        Assert.True(result.Data.TechnicalReview["problem1"].Solved);
        Assert.Equal("Practice narrating complexity while you code.", result.Data.NextSteps);

        var stored = await app.Db.MockInterviews.IgnoreQueryFilters().SingleAsync(m => m.Id == interview.Id);
        Assert.Equal(MockInterviewStatus.Completed, stored.Status);
        Assert.NotNull(stored.CompletedAt);
        Assert.Equal(80f, stored.Score);

        var mastery = await app.Db.StudentMasteries.IgnoreQueryFilters()
            .SingleAsync(m => m.ConceptId == concept.Id);
        Assert.Equal("user-1", mastery.UserId);
        Assert.True(mastery.MasteryProbability > 0.1f, $"expected BKT lift, got {mastery.MasteryProbability}");
        Assert.Equal(1, mastery.CorrectAttempts);
        Assert.Equal(0, mastery.IncorrectAttempts);
        Assert.NotNull(mastery.LastPracticedAt);
    }

    [Fact]
    public async Task Complete_ReadinessBefore_MatchesPriorScore_AndRisesAfter()
    {
        using var app = CreateApp();
        var concept = await SeedConceptAsync(app.Db);
        var question = await SeedQuestionAsync(app.Db, concept.Id);
        var jdId = await SeedJdAsync(app.Db, "user-1");
        await SeedSkillWeightAsync(app.Db, jdId, skillName: concept.Name, weight: 1f);
        await SeedMasteryAsync(app.Db, "user-1", concept.Id, probability: 0.5f);
        var interview = await SeedInterviewAsync(app.Db, jdId: jdId, questionId: question.Id);
        app.Generator.Review = Draft(solved: true);

        // The Done-when assertion: before == the score prior to the review.
        var prior = await app.Sender.Send(new GetReadinessQuery { JdId = jdId });
        Assert.True(prior.Success);
        Assert.Equal(50f, prior.Data!.Score);

        var result = await app.Sender.Send(new CompleteMockInterviewCommand { InterviewId = interview.Id });

        var delta = result.Data!.ReadinessDelta;
        Assert.Equal(prior.Data.Score, delta.Before);
        Assert.True(delta.After > delta.Before,
            $"expected readiness to rise after solved verdict, before={delta.Before} after={delta.After}");
        Assert.Equal(delta.After - delta.Before, delta.Delta);
    }
}
