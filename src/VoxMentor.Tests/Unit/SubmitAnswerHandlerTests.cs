using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Practice.SubmitAnswer;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SubmitAnswerHandler"/> covering correct/incorrect
/// answers, missing questions, unauthenticated users, validation, and
/// concurrent-submission retry handling.
/// </summary>
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

    /// <summary>Creates an InMemory database context, optionally shared by name for cross-context tests.</summary>
    private static Infrastructure.Persistence.ApplicationDbContext CreateDb(string? sharedName = null)
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(sharedName ?? Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options);
    }

    /// <summary>Seeds a single practice question with a default (or given) concept.</summary>
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

    /// <summary>Seeds a graded code submission for the given question.</summary>
    private static async Task<CodeSubmission> SeedSubmissionAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        Question question,
        string userId = "user-1",
        bool isCorrect = true,
        SubmissionStatus status = SubmissionStatus.Accepted)
    {
        var submission = new CodeSubmission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            QuestionId = question.Id,
            Code = "print(10)",
            Language = "python",
            IsCorrect = isCorrect,
            TestCasesPassed = isCorrect ? 1 : 0,
            TestCasesTotal = 1,
            Status = status
        };
        db.CodeSubmissions.Add(submission);
        await db.SaveChangesAsync();
        return submission;
    }

    /// <summary>Builds a handler over the given context with optional user/publisher fakes.</summary>
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
    public async Task Handle_WithCodeSubmissionId_DerivesCorrectnessAndClaims()
    {
        using var db = CreateDb();
        var question = await SeedQuestionAsync(db);
        // Submission says incorrect; command's IsCorrect (true) must be ignored.
        var submission = await SeedSubmissionAsync(db, question, isCorrect: false);
        var publisher = new FakeEventPublisher();
        var handler = CreateHandler(db, publisher: publisher);

        var response = await handler.Handle(
            new SubmitAnswerCommand(question.Id, true, submission.Id), CancellationToken.None);

        Assert.True(response.Success);
        Assert.False(response.Data!.IsCorrect is true);
        Assert.Equal(0, response.Data.CorrectAttempts);
        Assert.Equal(1, response.Data.IncorrectAttempts);
        Assert.Equal(1, publisher.PublishedCount);

        var stored = await db.CodeSubmissions.SingleAsync();
        Assert.NotNull(stored.MasteryAppliedAt);
        Assert.NotNull(stored.MasteryBefore);
        Assert.NotNull(stored.MasteryAfter);
        Assert.Equal(response.Data.PreviousMastery, stored.MasteryBefore!.Value);
        Assert.Equal(response.Data.NewMastery, stored.MasteryAfter!.Value);
        Assert.Equal(response.Data.CorrectAttempts, stored.CorrectAttemptsAfter);
        Assert.Equal(response.Data.IncorrectAttempts, stored.IncorrectAttemptsAfter);
    }

    [Fact]
    public async Task Handle_DuplicateCodeSubmissionId_AppliesOnceAndReturnsOriginalResult()
    {
        using var db = CreateDb();
        var question = await SeedQuestionAsync(db);
        var submission = await SeedSubmissionAsync(db, question, isCorrect: true);
        var publisher = new FakeEventPublisher();
        var handler = CreateHandler(db, publisher: publisher);

        var first = await handler.Handle(
            new SubmitAnswerCommand(question.Id, false, submission.Id), CancellationToken.None);
        var firstAppliedAt = (await db.CodeSubmissions.SingleAsync()).MasteryAppliedAt;

        var second = await handler.Handle(
            new SubmitAnswerCommand(question.Id, false, submission.Id), CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.Data, second.Data);
        Assert.True(first.Data!.IsCorrect is true);

        var stored = await db.CodeSubmissions.SingleAsync();
        Assert.Equal(firstAppliedAt, stored.MasteryAppliedAt);
        Assert.Equal(1, publisher.PublishedCount);

        var mastery = await db.StudentMasteries.SingleAsync();
        Assert.Equal(1, mastery.CorrectAttempts);
        Assert.Equal(0, mastery.IncorrectAttempts);
    }

    [Fact]
    public async Task Handle_CodeSubmissionOwnedByOtherUser_ThrowsNotFound()
    {
        using var db = CreateDb();
        var question = await SeedQuestionAsync(db);
        var submission = await SeedSubmissionAsync(db, question, userId: "user-2");
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<Application.Common.Exceptions.NotFoundException>(
            () => handler.Handle(new SubmitAnswerCommand(question.Id, true, submission.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_CodeSubmissionForDifferentQuestion_ThrowsValidation()
    {
        using var db = CreateDb();
        var question = await SeedQuestionAsync(db);
        var otherQuestion = await SeedQuestionAsync(db);
        var submission = await SeedSubmissionAsync(db, otherQuestion);
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<Application.Common.Exceptions.ValidationException>(
            () => handler.Handle(new SubmitAnswerCommand(question.Id, true, submission.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UnknownCodeSubmission_ThrowsNotFound()
    {
        using var db = CreateDb();
        var question = await SeedQuestionAsync(db);
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<Application.Common.Exceptions.NotFoundException>(
            () => handler.Handle(new SubmitAnswerCommand(question.Id, true, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_PendingCodeSubmission_DoesNotApplyMastery()
    {
        using var db = CreateDb();
        var question = await SeedQuestionAsync(db);
        var submission = await SeedSubmissionAsync(
            db, question, isCorrect: false, status: SubmissionStatus.Pending);
        var publisher = new FakeEventPublisher();
        var handler = CreateHandler(db, publisher: publisher);

        var response = await handler.Handle(
            new SubmitAnswerCommand(question.Id, true, submission.Id), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Null(response.Data!.IsCorrect);
        Assert.Equal(
            "Submission has no test results. Mastery unchanged.",
            response.Message);
        Assert.Equal(0, response.Data.CorrectAttempts);
        Assert.Equal(0, response.Data.IncorrectAttempts);
        Assert.Equal(0, publisher.PublishedCount);
        Assert.Empty(db.StudentMasteries);
        Assert.Null((await db.CodeSubmissions.SingleAsync()).MasteryAppliedAt);
    }

    /// <summary>
    /// Decorator over <see cref="IApplicationDbContext"/> that injects a fault on
    /// the Nth SaveChangesAsync call to test retry paths deterministically.
    /// </summary>
    private sealed class FlakyDbContext : IApplicationDbContext
    {
        private readonly IApplicationDbContext _inner;
        private readonly Func<int, Exception?> _fault;
        private int _saves;

        public FlakyDbContext(IApplicationDbContext inner, Func<int, Exception?> fault)
        {
            _inner = inner;
            _fault = fault;
        }

        public DbSet<ApplicationUser> Users => _inner.Users;
        public DbSet<RefreshToken> RefreshTokens => _inner.RefreshTokens;
        public DbSet<Concept> Concepts => _inner.Concepts;
        public DbSet<Prerequisite> Prerequisites => _inner.Prerequisites;
        public DbSet<Question> Questions => _inner.Questions;
        public DbSet<StudentMastery> StudentMasteries => _inner.StudentMasteries;
        public DbSet<CodeSubmission> CodeSubmissions => _inner.CodeSubmissions;
        public DbSet<MockInterview> MockInterviews => _inner.MockInterviews;
        public DbSet<AuditLog> AuditLogs => _inner.AuditLogs;
        public DbSet<BktParameters> BktParameters => _inner.BktParameters;
        public DbSet<JobDescription> JobDescriptions => _inner.JobDescriptions;
        public DbSet<JdSkillWeight> JdSkillWeights => _inner.JdSkillWeights;
        public DbSet<TutorSession> TutorSessions => _inner.TutorSessions;

        /// <summary>Throws the injected fault (if any) before delegating the save.</summary>
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var fault = _fault(++_saves);
            if (fault is not null)
                throw fault;
            return await _inner.SaveChangesAsync(cancellationToken);
        }

        public void ClearChangeTracker() => _inner.ClearChangeTracker();

        public EntityEntry<T> Entry<T>(T entity) where T : class => _inner.Entry(entity);

        public IQueryable<T> SqlQueryRaw<T>(string sql, params object[] parameters) where T : class
            => _inner.SqlQueryRaw<T>(sql, parameters);
    }

    [Fact]
    public async Task Handle_ConcurrencyConflictOnce_RetriesAndSucceeds()
    {
        using var db = CreateDb();
        var question = await SeedQuestionAsync(db);
        db.StudentMasteries.Add(new StudentMastery
        {
            UserId = "user-1",
            ConceptId = question.ConceptId,
            MasteryProbability = 0.5f
        });
        await db.SaveChangesAsync();

        var flaky = new FlakyDbContext(db, saveNumber =>
            saveNumber == 1 ? new DbUpdateConcurrencyException("simulated conflict") : null);
        var publisher = new FakeEventPublisher();
        var handler = new SubmitAnswerHandler(db: flaky, bktEngine: new BktEngine(), currentUser: new FakeCurrentUserService(), eventPublisher: publisher);

        var response = await handler.Handle(new SubmitAnswerCommand(question.Id, true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(1, response.Data!.CorrectAttempts);
        Assert.NotEqual(0.5f, response.Data.NewMastery);
        Assert.Equal(1, publisher.PublishedCount);
    }

    [Fact]
    public async Task Handle_UniqueViolationOnce_RetriesAsUpdate()
    {
        using var db = CreateDb();
        var question = await SeedQuestionAsync(db);
        db.StudentMasteries.Add(new StudentMastery
        {
            UserId = "user-1",
            ConceptId = question.ConceptId,
            MasteryProbability = 0.5f
        });
        await db.SaveChangesAsync();

        var flaky = new FlakyDbContext(db, saveNumber =>
            saveNumber == 1 ? new DbUpdateException("duplicate key value violates unique constraint") : null);
        var handler = new SubmitAnswerHandler(db: flaky, bktEngine: new BktEngine(), currentUser: new FakeCurrentUserService(), eventPublisher: new FakeEventPublisher());

        var response = await handler.Handle(new SubmitAnswerCommand(question.Id, false), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(1, response.Data!.IncorrectAttempts);
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

        // Force genuine overlap: both handlers read the shared initial state and
        // reach SaveChangesAsync before either persists, so the loser must take
        // the concurrency retry path instead of overwriting the winner's update.
        var saveBarrier = new AsyncBarrier(participants: 2);
        var handler1 = new SubmitAnswerHandler(
            new SaveBarrierDbContext(db1, saveBarrier), new BktEngine(), new FakeCurrentUserService(), publisher1);
        var handler2 = new SubmitAnswerHandler(
            new SaveBarrierDbContext(db2, saveBarrier), new BktEngine(), new FakeCurrentUserService(), publisher2);

        var task1 = handler1.Handle(new SubmitAnswerCommand(questionId, true), CancellationToken.None);
        var task2 = handler2.Handle(new SubmitAnswerCommand(questionId, false), CancellationToken.None);

        var responses = await Task.WhenAll(
            task1.WaitAsync(TimeSpan.FromSeconds(30)),
            task2.WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.All(responses, r => Assert.True(r.Success));

        using var verifyDb = CreateDb(storeName);
        var stored = await verifyDb.StudentMasteries.FirstAsync();
        Assert.Equal(1, stored.CorrectAttempts);
        Assert.Equal(1, stored.IncorrectAttempts);
        Assert.Equal(2, publisher1.PublishedCount + publisher2.PublishedCount);
    }

    [Fact]
    public async Task Handle_ConcurrentSameCodeSubmission_ExactlyOneAppliesMastery()
    {
        var storeName = Guid.NewGuid().ToString();
        Guid questionId;
        Guid submissionId;
        using (var seedDb = CreateDb(storeName))
        {
            var question = await SeedQuestionAsync(seedDb);
            var submission = await SeedSubmissionAsync(seedDb, question);
            questionId = question.Id;
            submissionId = submission.Id;
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

        // Force overlap at the save boundary: both handlers pass the unclaimed
        // check, then race the mastery write + claim. Exactly one may win.
        var saveBarrier = new AsyncBarrier(participants: 2);
        var handler1 = new SubmitAnswerHandler(
            new SaveBarrierDbContext(db1, saveBarrier), new BktEngine(), new FakeCurrentUserService(), publisher1);
        var handler2 = new SubmitAnswerHandler(
            new SaveBarrierDbContext(db2, saveBarrier), new BktEngine(), new FakeCurrentUserService(), publisher2);

        var task1 = handler1.Handle(new SubmitAnswerCommand(questionId, true, submissionId), CancellationToken.None);
        var task2 = handler2.Handle(new SubmitAnswerCommand(questionId, true, submissionId), CancellationToken.None);

        var responses = await Task.WhenAll(
            task1.WaitAsync(TimeSpan.FromSeconds(30)),
            task2.WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.All(responses, r => Assert.True(r.Success));
        Assert.Equal(responses[0].Data, responses[1].Data);

        using var verifyDb = CreateDb(storeName);
        var mastery = await verifyDb.StudentMasteries.SingleAsync();
        Assert.Equal(1, mastery.CorrectAttempts);
        Assert.Equal(0, mastery.IncorrectAttempts);

        var stored = await verifyDb.CodeSubmissions.SingleAsync();
        Assert.NotNull(stored.MasteryAppliedAt);
        Assert.NotNull(stored.MasteryAfter);
        Assert.Equal(1, publisher1.PublishedCount + publisher2.PublishedCount);
    }

    [Fact]
    public async Task Handle_ReplayAfterInterveningAnswer_ReturnsOriginalSnapshot()
    {
        using var db = CreateDb();
        var question = await SeedQuestionAsync(db);
        var submission = await SeedSubmissionAsync(db, question, isCorrect: true);
        var publisher = new FakeEventPublisher();
        var handler = CreateHandler(db, publisher: publisher);

        var first = await handler.Handle(
            new SubmitAnswerCommand(question.Id, true, submission.Id), CancellationToken.None);

        // An unrelated answer on the same concept lands before the duplicate replays.
        await handler.Handle(
            new SubmitAnswerCommand(question.Id, false), CancellationToken.None);
        var publishesAfterIntervening = publisher.PublishedCount;

        var replay = await handler.Handle(
            new SubmitAnswerCommand(question.Id, true, submission.Id), CancellationToken.None);

        Assert.Equal(first.Data, replay.Data);
        Assert.Equal(publishesAfterIntervening, publisher.PublishedCount); // replay published nothing
        var mastery = await db.StudentMasteries.SingleAsync();
        Assert.Equal(1, mastery.IncorrectAttempts); // intervening answer did apply
    }

    /// <summary>Migration-backfilled claim (no snapshot): replay must be labeled, not passed off as the original result.</summary>
    [Fact]
    public async Task Handle_LegacyClaimWithoutSnapshot_LabelsResultAsUnavailable()
    {
        using var db = CreateDb();
        var question = await SeedQuestionAsync(db);
        var submission = await SeedSubmissionAsync(db, question, isCorrect: true);
        submission.MasteryAppliedAt = submission.CreatedAt;
        db.StudentMasteries.Add(new StudentMastery
        {
            UserId = "user-1",
            ConceptId = question.ConceptId,
            MasteryProbability = 0.9f,
            CorrectAttempts = 5
        });
        await db.SaveChangesAsync();

        var publisher = new FakeEventPublisher();
        var handler = CreateHandler(db, publisher: publisher);
        var replay = await handler.Handle(
            new SubmitAnswerCommand(question.Id, true, submission.Id), CancellationToken.None);

        Assert.True(replay.Success);
        Assert.Equal(
            "Answer already recorded. Original mastery result unavailable; showing current mastery.",
            replay.Message);
        Assert.Equal(0.9f, replay.Data!.PreviousMastery);
        Assert.Equal(0.9f, replay.Data.NewMastery);
        Assert.Equal(0f, replay.Data.MasteryDelta);
        Assert.Equal(0, publisher.PublishedCount);

        var mastery = await db.StudentMasteries.SingleAsync();
        Assert.Equal(5, mastery.CorrectAttempts);
        Assert.Equal(0.9f, mastery.MasteryProbability);
    }

    /// <summary>
    /// Async barrier for a fixed number of participants: each caller registers
    /// arrival and resumes only once every participant has arrived. Single-phase
    /// (single-use); signals after release pass through immediately.
    /// </summary>
    private sealed class AsyncBarrier
    {
        private readonly int _participants;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public AsyncBarrier(int participants) => _participants = participants;

        /// <summary>Registers arrival and completes once all participants have arrived.</summary>
        public Task SignalAndWaitAsync()
        {
            if (Interlocked.Increment(ref _arrived) == _participants)
            {
                _release.TrySetResult();
            }
            return _release.Task;
        }
    }

    /// <summary>
    /// Decorator that holds <see cref="SaveChangesAsync"/> at a shared barrier so
    /// concurrent handlers are forced to overlap at the save boundary (both having
    /// read the same initial state) before either persists.
    /// </summary>
    private sealed class SaveBarrierDbContext : IApplicationDbContext
    {
        private readonly IApplicationDbContext _inner;
        private readonly AsyncBarrier _barrier;

        public SaveBarrierDbContext(IApplicationDbContext inner, AsyncBarrier barrier)
        {
            _inner = inner;
            _barrier = barrier;
        }

        public DbSet<ApplicationUser> Users => _inner.Users;
        public DbSet<RefreshToken> RefreshTokens => _inner.RefreshTokens;
        public DbSet<Concept> Concepts => _inner.Concepts;
        public DbSet<Prerequisite> Prerequisites => _inner.Prerequisites;
        public DbSet<Question> Questions => _inner.Questions;
        public DbSet<StudentMastery> StudentMasteries => _inner.StudentMasteries;
        public DbSet<CodeSubmission> CodeSubmissions => _inner.CodeSubmissions;
        public DbSet<MockInterview> MockInterviews => _inner.MockInterviews;
        public DbSet<AuditLog> AuditLogs => _inner.AuditLogs;
        public DbSet<BktParameters> BktParameters => _inner.BktParameters;
        public DbSet<JobDescription> JobDescriptions => _inner.JobDescriptions;
        public DbSet<JdSkillWeight> JdSkillWeights => _inner.JdSkillWeights;
        public DbSet<TutorSession> TutorSessions => _inner.TutorSessions;

        /// <summary>Waits until all barrier participants reach the save boundary, then saves.</summary>
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await _barrier.SignalAndWaitAsync();
            return await _inner.SaveChangesAsync(cancellationToken);
        }

        public void ClearChangeTracker() => _inner.ClearChangeTracker();

        public EntityEntry<T> Entry<T>(T entity) where T : class => _inner.Entry(entity);

        public IQueryable<T> SqlQueryRaw<T>(string sql, params object[] parameters) where T : class
            => _inner.SqlQueryRaw<T>(sql, parameters);
    }
}
