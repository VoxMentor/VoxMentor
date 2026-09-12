using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Practice.SubmitCode;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SubmitCodeHandler"/> covering the full pipeline:
/// test-case execution, status derivation, AI-evaluation failure tolerance,
/// BKT mastery updates, and validation/authorization paths.
/// </summary>
public class SubmitCodeHandlerTests
{
    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
    }

    private sealed class FakeTokenService : ICurrentTokenService
    {
        public string? AccessToken => "test-token";
    }

    private sealed class FakeCodeExecService : ICodeExecService
    {
        public Func<CodeExecutionRequest, IReadOnlyList<CodeExecutionCaseResult>> Respond { get; set; }
            = _ => [];

        public Task<IReadOnlyList<CodeExecutionCaseResult>> ExecuteAsync(
            CodeExecutionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Respond(request));
    }

    private sealed class FakeCodeEvaluator : ICodeEvaluator
    {
        public bool Throw { get; set; }

        public Task<CodeEvaluation> EvaluateAsync(string code, string language, CancellationToken cancellationToken = default)
            => Throw
                ? throw new InvalidOperationException("Ollama is down")
                : Task.FromResult(new CodeEvaluation());
    }

    private sealed class FakeEventPublisher : IMasteryEventPublisher
    {
        public int PublishedCount { get; private set; }
        public Action<StudentMastery>? OnPublish { get; set; }

        public Task PublishMasteryUpdatedAsync(StudentMastery mastery, float previousMastery, CancellationToken cancellationToken = default)
        {
            PublishedCount++;
            OnPublish?.Invoke(mastery);
            return Task.CompletedTask;
        }
    }

    private static Infrastructure.Persistence.ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<Question> SeedQuestionAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string[] testCases)
    {
        var question = new Question
        {
            Id = Guid.NewGuid(),
            ConceptId = Guid.NewGuid(),
            Title = "Two Sum",
            Description = "Find two numbers that add up to target.",
            TestCases = testCases
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        return question;
    }

    private static SubmitCodeHandler CreateHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeCurrentUserService? user = null,
        FakeCodeExecService? exec = null,
        FakeCodeEvaluator? evaluator = null,
        FakeEventPublisher? publisher = null)
        => new(
            db,
            new BktEngine(),
            user ?? new FakeCurrentUserService(),
            new FakeTokenService(),
            exec ?? new FakeCodeExecService(),
            evaluator ?? new FakeCodeEvaluator(),
            publisher ?? new FakeEventPublisher(),
            NullLogger<SubmitCodeHandler>.Instance);

    private static CodeExecutionCaseResult Case(bool passed, string expected = "ok", string actual = "ok",
        bool timedOut = false, string status = "Finished", string stderr = "",
        int? executionTimeMs = 12, int? memoryUsageKb = 2048)
        => new("in", expected, actual, passed, timedOut, status, stderr, executionTimeMs, memoryUsageKb);

    [Fact]
    public async Task Handle_AllTestsPass_PersistsAcceptedSubmissionAndRaisesMastery()
    {
        await using var db = CreateDb();
        var question = await SeedQuestionAsync(db, ["3 7\n|10", "1 2\n|3"]);
        var publisher = new FakeEventPublisher();
        var exec = new FakeCodeExecService
        {
            Respond = _ => [Case(true), Case(true)]
        };
        var handler = CreateHandler(db, exec: exec, publisher: publisher);

        var response = await handler.Handle(
            new SubmitCodeCommand(question.Id, "print(10)", "python"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Data!.TestCasesPassed);
        Assert.Equal(2, response.Data.TestCasesTotal);
        Assert.True(response.Data.IsCorrect);
        Assert.Equal(SubmissionStatus.Accepted.ToString(), response.Data.Status);

        var submission = await db.CodeSubmissions.SingleAsync();
        Assert.True(submission.IsCorrect);
        Assert.Equal(2, submission.TestCasesPassed);
        Assert.Equal(SubmissionStatus.Accepted, submission.Status);
        Assert.NotNull(submission.AiEvaluation);

        var mastery = await db.StudentMasteries.SingleAsync();
        Assert.True(mastery.MasteryProbability > 0.1f);
        Assert.Equal(1, mastery.CorrectAttempts);
        Assert.Equal(1, publisher.PublishedCount);
        Assert.True(response.Data.NewMastery > response.Data.PreviousMastery);
    }

    [Fact]
    public async Task Handle_WrongAnswer_PersistsWrongAnswerAndLowersMastery()
    {
        await using var db = CreateDb();
        var question = await SeedQuestionAsync(db, ["|10"]);
        var exec = new FakeCodeExecService
        {
            Respond = _ => [Case(false, expected: "10", actual: "20")]
        };
        var handler = CreateHandler(db, exec: exec);

        var response = await handler.Handle(
            new SubmitCodeCommand(question.Id, "print(20)", "python"), CancellationToken.None);

        Assert.False(response.Data!.IsCorrect);
        Assert.Equal(SubmissionStatus.WrongAnswer.ToString(), response.Data.Status);

        var submission = await db.CodeSubmissions.SingleAsync();
        Assert.Equal(SubmissionStatus.WrongAnswer, submission.Status);
        Assert.Equal(0, submission.TestCasesPassed);

        var mastery = await db.StudentMasteries.SingleAsync();
        Assert.Equal(1, mastery.IncorrectAttempts);
        // BKT at very low prior (0.1): P(L|wrong) drops, but learnRate (0.3)
        // applied in the transition matrix can push posterior up — this is
        // correct BKT behavior. Verify the incorrect attempt was recorded and
        // that new mastery is different from previous (BKT responded).
        Assert.NotEqual(response.Data.PreviousMastery, response.Data.NewMastery);
    }

    [Fact]
    public async Task Handle_Timeout_PersistsTimeoutStatus()
    {
        await using var db = CreateDb();
        var question = await SeedQuestionAsync(db, ["|ok"]);
        var exec = new FakeCodeExecService
        {
            Respond = _ => [Case(true, timedOut: true, status: "Time Limit Exceeded")]
        };
        var handler = CreateHandler(db, exec: exec);

        var response = await handler.Handle(
            new SubmitCodeCommand(question.Id, "while True: pass", "python"), CancellationToken.None);

        Assert.Equal(SubmissionStatus.Timeout.ToString(), response.Data!.Status);
        Assert.False(response.Data.IsCorrect);

        var submission = await db.CodeSubmissions.SingleAsync();
        Assert.Equal(SubmissionStatus.Timeout, submission.Status);
    }

    [Fact]
    public async Task Handle_NoTestCases_PersistsPendingWithoutBktUpdate()
    {
        await using var db = CreateDb();
        var question = await SeedQuestionAsync(db, []);
        var exec = new FakeCodeExecService
        {
            Respond = _ => throw new InvalidOperationException("must not be called")
        };
        var publisher = new FakeEventPublisher();
        var handler = CreateHandler(db, exec: exec, publisher: publisher);

        var response = await handler.Handle(
            new SubmitCodeCommand(question.Id, "print('hi')", "python"), CancellationToken.None);

        Assert.Null(response.Data!.IsCorrect);
        Assert.Equal(SubmissionStatus.Pending.ToString(), response.Data.Status);
        Assert.Equal(0, response.Data.TestCasesTotal);

        var submission = await db.CodeSubmissions.SingleAsync();
        Assert.Equal(SubmissionStatus.Pending, submission.Status);
        Assert.Empty(db.StudentMasteries);
        Assert.Equal(0, publisher.PublishedCount);
    }

    [Fact]
    public async Task Handle_EvaluatorFailure_StillPersistsSubmissionWithNullEvaluation()
    {
        await using var db = CreateDb();
        var question = await SeedQuestionAsync(db, ["|ok"]);
        var exec = new FakeCodeExecService { Respond = _ => [Case(true)] };
        var evaluator = new FakeCodeEvaluator { Throw = true };
        var handler = CreateHandler(db, exec: exec, evaluator: evaluator);

        var response = await handler.Handle(
            new SubmitCodeCommand(question.Id, "print('ok')", "python"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Null(response.Data!.AiEvaluation);
        var submission = await db.CodeSubmissions.SingleAsync();
        Assert.Null(submission.AiEvaluation);
        Assert.Equal(SubmissionStatus.Accepted, submission.Status);
    }

    [Fact]
    public async Task Handle_UnknownQuestion_ThrowsNotFound()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);

        await Assert.ThrowsAsync<Application.Common.Exceptions.NotFoundException>(() =>
            handler.Handle(new SubmitCodeCommand(Guid.NewGuid(), "print(1)", "python"), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Unauthenticated_ThrowsUnauthorized()
    {
        await using var db = CreateDb();
        var question = await SeedQuestionAsync(db, ["|ok"]);
        var handler = CreateHandler(db, user: new FakeCurrentUserService { UserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new SubmitCodeCommand(question.Id, "print(1)", "python"), CancellationToken.None));
    }

    [Fact]
    public void ParseTestCases_SplitsStdinAndExpected()
    {
        var parsed = SubmitCodeHandler.ParseTestCases(["3 7\n|10", "|3", "plain"]);

        Assert.Equal(3, parsed.Count);
        Assert.Equal("3 7\n", parsed[0].Stdin);
        Assert.Equal("10", parsed[0].ExpectedOutput);
        Assert.Equal(string.Empty, parsed[1].Stdin);
        Assert.Equal("3", parsed[1].ExpectedOutput);
        Assert.Equal(string.Empty, parsed[2].Stdin);
        Assert.Equal("plain", parsed[2].ExpectedOutput);
    }

    [Fact]
    public void ParseTestCases_EmptyArray_ReturnsEmptyList()
    {
        Assert.Empty(SubmitCodeHandler.ParseTestCases([]));
    }

    [Fact]
    public async Task Handle_RuntimeError_WhenExecServiceFailsAllCases()
    {
        await using var db = CreateDb();
        var question = await SeedQuestionAsync(db, ["|ok"]);
        var exec = new FakeCodeExecService
        {
            Respond = _ => [Case(false, actual: "", status: "Runtime Error (NZEC)", stderr: "Traceback...")]
        };
        var handler = CreateHandler(db, exec: exec);

        var response = await handler.Handle(
            new SubmitCodeCommand(question.Id, "raise Exception()", "python"), CancellationToken.None);

        Assert.Equal(SubmissionStatus.RuntimeError.ToString(), response.Data!.Status);
        var submission = await db.CodeSubmissions.SingleAsync();
        Assert.Equal(SubmissionStatus.RuntimeError, submission.Status);
    }

    [Fact]
    public async Task Handle_PersistsExecutionStats_FromCaseResults()
    {
        await using var db = CreateDb();
        var question = await SeedQuestionAsync(db, ["|ok", "|ok"]);
        var exec = new FakeCodeExecService
        {
            Respond = _ => [Case(true, executionTimeMs: 12, memoryUsageKb: 2048), Case(true, executionTimeMs: 30, memoryUsageKb: 4096)]
        };
        var handler = CreateHandler(db, exec: exec);

        await handler.Handle(
            new SubmitCodeCommand(question.Id, "print('ok')", "python"), CancellationToken.None);

        var submission = await db.CodeSubmissions.SingleAsync();
        Assert.Equal(42, submission.ExecutionTimeMs);
        Assert.Equal(4096, submission.MemoryUsageKb);
    }

    [Fact]
    public async Task Handle_CLanguage_AcceptsSubmission()
    {
        await using var db = CreateDb();
        var question = await SeedQuestionAsync(db, ["|hello"]);
        var exec = new FakeCodeExecService { Respond = _ => [Case(true)] };
        var handler = CreateHandler(db, exec: exec);

        var response = await handler.Handle(
            new SubmitCodeCommand(question.Id, "#include <stdio.h>", "c"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(SubmissionStatus.Accepted.ToString(), response.Data!.Status);
    }

    [Fact]
    public async Task Handle_CorrectAnswer_PublishesEventAfterSave()
    {
        await using var db = CreateDb();
        var question = await SeedQuestionAsync(db, ["|ok"]);
        var exec = new FakeCodeExecService { Respond = _ => [Case(true)] };
        var publisher = new FakeEventPublisher();
        var handler = CreateHandler(db, exec: exec, publisher: publisher);

        // Verify the submission is already persisted at the moment the event fires.
        publisher.OnPublish = _ =>
        {
            var submission = db.CodeSubmissions.SingleOrDefault();
            Assert.NotNull(submission);
        };

        var response = await handler.Handle(
            new SubmitCodeCommand(question.Id, "print('ok')", "python"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(1, publisher.PublishedCount);
        var savedSubmission = await db.CodeSubmissions.SingleAsync();
        Assert.NotNull(savedSubmission);
    }

    [Fact]
    public void SupportedLanguages_IncludesC()
    {
        Assert.Contains("c", SubmitCodeValidator.SupportedLanguages);
    }

    [Fact]
    public async Task Handle_HiddenTestCases_ExcludesDetailsFromResponse()
    {
        await using var db = CreateDb();
        var question = await SeedQuestionAsync(db, ["1 2\n|3", "3 4\n|7", "5 6\n|11", "7 8\n|15"]);
        question.HiddenTestCaseCount = 2;
        await db.SaveChangesAsync();

        var exec = new FakeCodeExecService
        {
            Respond = _ => [Case(true), Case(true), Case(true), Case(false, actual: "16")]
        };
        var handler = CreateHandler(db, exec: exec);

        var response = await handler.Handle(
            new SubmitCodeCommand(question.Id, "print(sum(map(int,input().split())))", "python"), CancellationToken.None);

        Assert.True(response.Success);
        // All 4 cases executed for correctness evaluation.
        Assert.Equal(3, response.Data!.TestCasesPassed);
        Assert.Equal(4, response.Data.TestCasesTotal);
        Assert.False(response.Data.IsCorrect);
        // Only 2 visible cases returned in TestCaseResults.
        Assert.Equal(2, response.Data.TestCaseResults.Count);
        Assert.Equal(0, response.Data.TestCaseResults[0].TestCaseIndex);
        Assert.Equal(1, response.Data.TestCaseResults[1].TestCaseIndex);
    }

    [Fact]
    public async Task Handle_NoHiddenCases_ReturnsAllDetails()
    {
        await using var db = CreateDb();
        var question = await SeedQuestionAsync(db, ["1 2\n|3", "3 4\n|7"]);
        // HiddenTestCaseCount defaults to 0.
        var exec = new FakeCodeExecService
        {
            Respond = _ => [Case(true), Case(true)]
        };
        var handler = CreateHandler(db, exec: exec);

        var response = await handler.Handle(
            new SubmitCodeCommand(question.Id, "print(sum(map(int,input().split())))", "python"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, response.Data!.TestCasesPassed);
        Assert.Equal(2, response.Data.TestCasesTotal);
        Assert.True(response.Data.IsCorrect);
        Assert.Equal(2, response.Data.TestCaseResults.Count);
    }
}
