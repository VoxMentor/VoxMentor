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
        return new Infrastructure.Persistence.ApplicationDbContext(options, new FakeCurrentUserService());
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

    private static async Task<JobDescription> SeedJdAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string userId = "user-1")
    {
        var jd = new JobDescription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompanyName = "Acme",
            Role = "Backend Engineer",
            RawText = "raw",
            Difficulty = "Mid",
            EstimatedWeeks = 8
        };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();
        return jd;
    }

    private static async Task SeedWeightAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        Guid jdId,
        string skillName,
        float weight)
    {
        db.JdSkillWeights.Add(new JdSkillWeight
        {
            Id = Guid.NewGuid(),
            JobDescriptionId = jdId,
            SkillName = skillName,
            Weight = weight
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Builds a handler with a seeded <see cref="Random"/> so the ε roll is
    /// reproducible. Default seed keeps the pre-#119 tests deterministic
    /// (their no-JD path never rolls anyway).
    /// </summary>
    private static GetNextQuestionHandler CreateHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeCurrentUserService? user = null,
        int seed = 0)
        => new(db, user ?? new FakeCurrentUserService(), new Random(seed));

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
    public async Task Handle_WithConceptId_PicksThatConceptEvenIfNotWeakest()
    {
        using var db = CreateDb();
        var c1 = await SeedConceptAsync(db, "Arrays");
        var c2 = await SeedConceptAsync(db, "DP");
        await SeedMasteryAsync(db, "user-1", c1.Id, 0.9f);
        await SeedMasteryAsync(db, "user-1", c2.Id, 0.1f);
        await SeedQuestionAsync(db, c1.Id, difficulty: 3, title: "ArrayQ");
        await SeedQuestionAsync(db, c2.Id, difficulty: 3, title: "DPQ");
        var handler = CreateHandler(db);

        // DP is weakest, but filter pins to Arrays (near-mastered)
        var response = await handler.Handle(
            new GetNextQuestionQuery { ConceptId = c1.Id }, CancellationToken.None);

        Assert.Equal("Arrays", response.Data!.ConceptName);
        Assert.Equal("ArrayQ", response.Data.Title);
    }

    [Fact]
    public async Task Handle_ConceptIdWithNoQuestions_ThrowsNotFound()
    {
        using var db = CreateDb();
        var c1 = await SeedConceptAsync(db, "Arrays");
        await SeedConceptAsync(db, "Empty");
        await SeedQuestionAsync(db, c1.Id, difficulty: 3, title: "ArrayQ");
        var empty = db.Concepts.Single(c => c.Name == "Empty");
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new GetNextQuestionQuery { ConceptId = empty.Id }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ConceptId_AllAttempted_RetriesWithinConcept()
    {
        using var db = CreateDb();
        var c1 = await SeedConceptAsync(db, "Arrays");
        var c2 = await SeedConceptAsync(db, "DP");
        var q1 = await SeedQuestionAsync(db, c1.Id, difficulty: 3, title: "ArrayDone");
        await SeedQuestionAsync(db, c2.Id, difficulty: 3, title: "DPFresh");
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

        // Must stay within Arrays (retrying the attempted question) even though
        // the DP question is unanswered
        var response = await handler.Handle(
            new GetNextQuestionQuery { ConceptId = c1.Id }, CancellationToken.None);

        Assert.Equal("ArrayDone", response.Data!.Title);
        Assert.Equal("Arrays", response.Data.ConceptName);
    }

    [Fact]
    public async Task Handle_UnknownConceptId_ThrowsNotFound()
    {
        using var db = CreateDb();
        await SeedConceptAsync(db, "Arrays");
        await SeedQuestionAsync(db, db.Concepts.Single().Id, difficulty: 3, title: "ArrayQ");
        var handler = CreateHandler(db);

        // A stale/malformed ?conceptId must 404 (the practice page maps 404 to its
        // "no questions" card) instead of falling through to the weakest concept.
        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new GetNextQuestionQuery { ConceptId = Guid.NewGuid() }, CancellationToken.None));
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

    [Fact]
    public async Task Handle_ExcludeQuestionIds_NeverServesAnAlreadyAskedProblem()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        var easy = await SeedQuestionAsync(db, concept.Id, title: "Asked already");
        var other = await SeedQuestionAsync(db, concept.Id, difficulty: 5, title: "Not asked");
        var handler = CreateHandler(db);

        // Without the exclusion the closer-difficulty "Asked already" would win.
        var response = await handler.Handle(
            new GetNextQuestionQuery { ExcludeQuestionIds = new[] { easy.Id } }, CancellationToken.None);

        Assert.Equal(other.Id, response.Data!.QuestionId);

        // Every question excluded → nothing new to serve (#148 advance falls back to a repeat).
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new GetNextQuestionQuery { ExcludeQuestionIds = new[] { easy.Id, other.Id } }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ReturnsIsomorphicInstanceId()
    {
        using var db = CreateDb();
        var concept = await SeedConceptAsync(db);
        var question = await SeedQuestionAsync(db, concept.Id);
        question.VariantSeed = "seed-a";
        await db.SaveChangesAsync();
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetNextQuestionQuery(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Data!.IsomorphicInstanceId);
        // Deterministic: a re-fetch returns the same instance id (#120).
        var again = await handler.Handle(new GetNextQuestionQuery(), CancellationToken.None);
        Assert.Equal(response.Data.IsomorphicInstanceId, again.Data!.IsomorphicInstanceId);
    }

    private static async Task<List<string>> RunPicksAsync(
        Infrastructure.Persistence.ApplicationDbContext db, int seed, int runs)
    {
        var handler = CreateHandler(db, seed: seed);
        var titles = new List<string>(runs);
        for (var i = 0; i < runs; i++)
        {
            var response = await handler.Handle(new GetNextQuestionQuery(), CancellationToken.None);
            titles.Add(response.Data!.Title);
        }
        return titles;
    }

    private sealed class ThrowingRandom : Random
    {
        private static InvalidOperationException Rolled()
            => new("selector rolled without a JD for the current user.");

        public override int Next() => throw Rolled();

        public override int Next(int maxValue) => throw Rolled();

        public override int Next(int minValue, int maxValue) => throw Rolled();

        public override double NextDouble() => throw Rolled();

        public override float NextSingle() => throw Rolled();
    }

    [Fact]
    public async Task Handle_NoJd_NeverRolls()
    {
        using var db = CreateDb();
        var c1 = await SeedConceptAsync(db, "Arrays");
        var c2 = await SeedConceptAsync(db, "DP");
        await SeedQuestionAsync(db, c1.Id, difficulty: 3, title: "ArrayQ");
        await SeedQuestionAsync(db, c2.Id, difficulty: 5, title: "DPQ");
        var handler = new GetNextQuestionHandler(
            db, new FakeCurrentUserService(), new ThrowingRandom());

        var response = await handler.Handle(new GetNextQuestionQuery(), CancellationToken.None);

        // Selection is unchanged by #119 when the user has no JD.
        Assert.Equal("Arrays", response.Data!.ConceptName);
    }

    [Fact]
    public async Task Handle_OtherUsersJd_DoesNotInfluenceSelection()
    {
        using var db = CreateDb();
        var c1 = await SeedConceptAsync(db, "Arrays");
        var c2 = await SeedConceptAsync(db, "DP");
        await SeedQuestionAsync(db, c1.Id, difficulty: 3, title: "ArrayQ");
        await SeedQuestionAsync(db, c2.Id, difficulty: 5, title: "DPQ");

        // Another tenant's JD wants DP. user-1 has no JD, so the IUserOwned
        // global filter must hide this row entirely.
        var foreignJd = await SeedJdAsync(db, userId: "user-2");
        await SeedWeightAsync(db, foreignJd.Id, "DP", 0.95f);

        var handler = new GetNextQuestionHandler(
            db, new FakeCurrentUserService(), new ThrowingRandom());

        var response = await handler.Handle(new GetNextQuestionQuery(), CancellationToken.None);

        // ThrowingRandom fails the moment the selector rolls, and DP would win
        // the weighted rank if the foreign JD leaked through.
        Assert.Equal("Arrays", response.Data!.ConceptName);
    }

    [Fact]
    public async Task Handle_WithConceptIdAndJd_NeverEntersSelector()
    {
        using var db = CreateDb();
        var c1 = await SeedConceptAsync(db, "Arrays");
        var c2 = await SeedConceptAsync(db, "DP");
        await SeedMasteryAsync(db, "user-1", c1.Id, 0.9f);
        await SeedMasteryAsync(db, "user-1", c2.Id, 0.1f);
        await SeedQuestionAsync(db, c1.Id, difficulty: 3, title: "ArrayQ");
        await SeedQuestionAsync(db, c2.Id, difficulty: 3, title: "DPQ");
        var jd = await SeedJdAsync(db);
        await SeedWeightAsync(db, jd.Id, "DP", 0.95f);

        // A pinned conceptId short-circuits the selector, so no ε roll happens
        // even though this user has a JD.
        var handler = new GetNextQuestionHandler(
            db, new FakeCurrentUserService(), new ThrowingRandom());

        var response = await handler.Handle(
            new GetNextQuestionQuery { ConceptId = c1.Id }, CancellationToken.None);

        Assert.Equal("Arrays", response.Data!.ConceptName);
        Assert.Equal("ArrayQ", response.Data.Title);
    }

    [Fact]
    public async Task Handle_JdWeighted_SameSeed_ProducesIdenticalSequence()
    {
        using var db = CreateDb();
        var c1 = await SeedConceptAsync(db, "Arrays");
        var c2 = await SeedConceptAsync(db, "DP");
        var c3 = await SeedConceptAsync(db, "Graphs");
        var jd = await SeedJdAsync(db);
        await SeedWeightAsync(db, jd.Id, "Arrays", 0.4f);
        await SeedWeightAsync(db, jd.Id, "DP", 0.6f);
        await SeedWeightAsync(db, jd.Id, "Graphs", 0.8f);
        await SeedQuestionAsync(db, c1.Id, difficulty: 3, title: "ArrayQ");
        await SeedQuestionAsync(db, c2.Id, difficulty: 5, title: "DPQ");
        await SeedQuestionAsync(db, c3.Id, difficulty: 7, title: "GraphQ");

        var first = await RunPicksAsync(db, seed: 12345, runs: 40);
        var second = await RunPicksAsync(db, seed: 12345, runs: 40);

        Assert.Equal(first, second);
        // A constant sequence would mean ε is dead — the selector never explored.
        Assert.True(first.Distinct().Count() > 1,
            $"selector never explored: {string.Join(",", first)}");
    }

    [Fact]
    public async Task Handle_JdWeighted_HigherJdWeightBeatsEqualMastery()
    {
        using var db = CreateDb();
        var arrays = await SeedConceptAsync(db, "Arrays");
        var dp = await SeedConceptAsync(db, "DP");
        await SeedMasteryAsync(db, "user-1", arrays.Id, 0.5f);
        await SeedMasteryAsync(db, "user-1", dp.Id, 0.5f); // identical mastery
        var jd = await SeedJdAsync(db);
        await SeedWeightAsync(db, jd.Id, "DP", 0.35f); // DP is what the JD asks for
        await SeedQuestionAsync(db, arrays.Id, difficulty: 6, title: "ArrayQ");
        await SeedQuestionAsync(db, dp.Id, difficulty: 6, title: "DPQ");

        var dpPicks = 0;
        var arraysPicks = 0;
        for (var seed = 0; seed < 200; seed++)
        {
            var response = await CreateHandler(db, seed: seed)
                .Handle(new GetNextQuestionQuery(), CancellationToken.None);

            if (response.Data!.ConceptName == "DP") dpPicks++;
            else arraysPicks++;
        }

        // severity: DP = 0.35 × (1 − 0.5) = 0.175 vs Arrays = 0 × (1 − 0.5) = 0,
        // so the exploit branch (80%) plus its share of exploration favours DP.
        Assert.True(dpPicks >= 140, $"DP picked only {dpPicks}/200 times.");
        Assert.True(arraysPicks > 0, "exploration never fired.");
    }

    [Fact]
    public async Task Handle_JdWeighted_NeverTargetsConceptWithNoUnansweredQuestions()
    {
        using var db = CreateDb();
        var arrays = await SeedConceptAsync(db, "Arrays");
        var dp = await SeedConceptAsync(db, "DP");
        await SeedConceptAsync(db, "Empty"); // no questions at all
        await SeedMasteryAsync(db, "user-1", arrays.Id, 0.9f);
        await SeedMasteryAsync(db, "user-1", dp.Id, 0.9f); // "Empty" stays unpracticed
        var jd = await SeedJdAsync(db);
        await SeedWeightAsync(db, jd.Id, "Arrays", 0.9f);
        await SeedWeightAsync(db, jd.Id, "DP", 0.5f);
        // "Empty" is the JD's top skill (1.0 × (1 − 0.1) = 0.9 severity), so it
        // wins the exploit branch outright — yet it has nothing to serve, and
        // both branches must skip it rather than fall back to an unrelated topic.
        await SeedWeightAsync(db, jd.Id, "Empty", 1f);

        await SeedQuestionAsync(db, arrays.Id, difficulty: 9, title: "ArrayQ");
        await SeedQuestionAsync(db, dp.Id, difficulty: 2, title: "DPQ_easy");
        await SeedQuestionAsync(db, dp.Id, difficulty: 9, title: "DPQ_hard");

        var dpEasy = 0;
        var dpHard = 0;
        for (var seed = 0; seed < 200; seed++)
        {
            var response = await CreateHandler(db, seed: seed)
                .Handle(new GetNextQuestionQuery(), CancellationToken.None);

            switch (response.Data!.Title)
            {
                case "DPQ_easy": dpEasy++; break;
                case "DPQ_hard": dpHard++; break;
            }
        }

        // Selecting "Empty" (unpracticed → target difficulty 2) would make the
        // fallback serve DPQ_easy — which is exactly what an exploit branch
        // ranking all concepts does. The top eligible concept is Arrays
        // (target 9 → ArrayQ), and DP, also target 9, picks DPQ_hard.
        Assert.Equal(0, dpEasy);
        // ...and exploration must still have fired at all.
        Assert.True(dpHard > 0, "exploration never fired.");
    }
}
