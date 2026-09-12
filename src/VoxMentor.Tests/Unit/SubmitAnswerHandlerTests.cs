using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Practice.SubmitAnswer;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;

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

        /// <summary>Waits until all barrier participants reach the save boundary, then saves.</summary>
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await _barrier.SignalAndWaitAsync();
            return await _inner.SaveChangesAsync(cancellationToken);
        }

        public void ClearChangeTracker() => _inner.ClearChangeTracker();

        public EntityEntry<T> Entry<T>(T entity) where T : class => _inner.Entry(entity);
    }
}
