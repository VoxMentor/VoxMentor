using System.Runtime.CompilerServices;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Common.Services;
using VoxMentor.Application.Features.MockInterviews;
using VoxMentor.Application.Features.Practice.GetNextQuestion;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="AnswerMockInterviewHandler"/>: 404 for unknown and
/// foreign interviews, 409 once ended (or all problems answered), transcript
/// growth with order preserved, and the canned probe when Ollama is
/// unavailable (#124); plus #148 advancement — verdict-driven, budget-forced,
/// and honest interviewOngoing/QuestionId.
/// </summary>
public class AnswerMockInterviewHandlerTests
{
    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
    }

    private sealed class StubGenerator : IMockInterviewGenerator
    {
        public string? FollowUp { get; set; }

        public string? Opening { get; set; } = "NEXT PROBLEM INTRO";

        /// <summary>Verdict for #148; null = model unavailable (must not stall).</summary>
        public bool? Solved { get; set; }

        public async IAsyncEnumerable<TutorChunk> OpeningStreamAsync(
            string? company, string? role, string problemTitle,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            if (Opening is not null)
            {
                yield return new TutorChunk(Opening);
            }
        }

        public async IAsyncEnumerable<TutorChunk> FollowUpStreamAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            if (FollowUp is not null)
            {
                yield return new TutorChunk(FollowUp);
            }
        }

        public Task<bool?> AssessAnswerSolvedAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            InterviewType type,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Solved);

        public Task<MockInterviewReviewDraft?> ReviewAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            InterviewType type,
            string? company,
            string? role,
            CancellationToken cancellationToken = default)
            => Task.FromResult<MockInterviewReviewDraft?>(null);
    }

    /// <summary>Selector stand-in that records what the #148 advance asked for.</summary>
    private sealed class StubSender : ISender
    {
        public NextQuestionDto Next { get; set; } = NextDto();

        public IReadOnlyCollection<Guid>? LastExcluded { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetNextQuestionQuery query)
            {
                LastExcluded = query.ExcludeQuestionIds;
                return Task.FromResult((TResponse)(object)ApiResponse<NextQuestionDto>.SuccessResult(Next));
            }

            throw new NotSupportedException($"Unexpected request {request.GetType().Name}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException($"Unexpected request {request.GetType().Name}");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => throw new NotSupportedException($"Unexpected request {request.GetType().Name}");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static NextQuestionDto NextDto(Guid? questionId = null, string title = "Two Sum")
        => new(
            questionId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "Arrays",
            title,
            "Desc",
            "Code",
            3,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Guid.NewGuid());

    private static (ApplicationDbContext Db, AnswerMockInterviewHandler Handler, StubGenerator Generator, StubSender Sender) CreateHandler(
        string userId = "user-1")
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeCurrentUserService { UserId = userId });

        var generator = new StubGenerator();
        var sender = new StubSender();
        var session = new MockInterviewSessionService(
            db, new FakeCurrentUserService { UserId = userId },
            sender, generator, configuration: null!, new AsyncKeyedLock());
        return (db, new AnswerMockInterviewHandler(session), generator, sender);
    }

    private static async Task<MockInterview> SeedInterviewAsync(
        ApplicationDbContext db,
        string userId = "user-1",
        MockInterviewStatus status = MockInterviewStatus.InProgress,
        int answeredQuestions = 0,
        Guid? questionId = null)
    {
        var interview = new MockInterview
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = InterviewType.Technical,
            TotalQuestions = 3,
            AnsweredQuestions = answeredQuestions,
            Status = status,
            StartedAt = DateTime.UtcNow,
            TranscriptJson = JsonSerializer.Serialize(
                new List<TranscriptTurn>
                {
                    new(TranscriptTurn.InterviewerRole, "Opening problem.", questionId ?? Guid.NewGuid(), DateTime.UtcNow)
                },
                TranscriptTurn.JsonOptions)
        };
        db.MockInterviews.Add(interview);
        await db.SaveChangesAsync();
        return interview;
    }

    private static List<TranscriptTurn> ReadTranscript(MockInterview interview)
        => JsonSerializer.Deserialize<List<TranscriptTurn>>(interview.TranscriptJson!, TranscriptTurn.JsonOptions)!;

    [Fact]
    public async Task Answer_UnknownInterview_ThrowsNotFound()
    {
        var (db, handler, _, _) = CreateHandler();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new AnswerMockInterviewCommand
            {
                InterviewId = Guid.NewGuid(),
                Message = "my answer"
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Answer_OtherStudentsInterview_ThrowsNotFound()
    {
        var (db, handler, _, _) = CreateHandler();
        var foreign = await SeedInterviewAsync(db, userId: "user-2");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new AnswerMockInterviewCommand
            {
                InterviewId = foreign.Id,
                Message = "my answer"
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Answer_EndedInterview_ThrowsConflict()
    {
        var (db, handler, _, _) = CreateHandler();
        var interview = await SeedInterviewAsync(db, status: MockInterviewStatus.Completed);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new AnswerMockInterviewCommand
            {
                InterviewId = interview.Id,
                Message = "my answer"
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Answer_UnsolvedFirstAnswer_Probes_SecondAdvancesOnBudget()
    {
        var (db, handler, generator, sender) = CreateHandler();
        generator.FollowUp = "PROBE 1";
        var interview = await SeedInterviewAsync(db);

        var first = await handler.Handle(new AnswerMockInterviewCommand
        {
            InterviewId = interview.Id,
            Message = "answer one"
        }, CancellationToken.None);

        Assert.Equal(3, first.Data!.TurnNumber); // opening + candidate + probe
        Assert.Equal("PROBE 1", first.Data.InterviewerMessage);
        Assert.True(first.Data.InterviewOngoing);

        var second = await handler.Handle(new AnswerMockInterviewCommand
        {
            InterviewId = interview.Id,
            Message = "answer two"
        }, CancellationToken.None);

        // Budget spent (one probe on problem 1) → advance even though Solved=false.
        Assert.Equal(5, second.Data!.TurnNumber);
        Assert.Equal("NEXT PROBLEM INTRO", second.Data.InterviewerMessage);
        Assert.True(second.Data.InterviewOngoing);
        Assert.Equal(sender.Next.QuestionId, second.Data.QuestionId);

        var stored = await db.MockInterviews.IgnoreQueryFilters().SingleAsync(m => m.Id == interview.Id);
        Assert.Equal(1, stored.AnsweredQuestions);

        var turns = ReadTranscript(stored);
        Assert.Equal(5, turns.Count);
        Assert.Equal(new[]
        {
            TranscriptTurn.InterviewerRole,
            TranscriptTurn.CandidateRole,
            TranscriptTurn.InterviewerRole,
            TranscriptTurn.CandidateRole,
            TranscriptTurn.InterviewerRole
        }, turns.Select(t => t.Role).ToArray());
        Assert.Equal(new[] { "answer one", "answer two" },
            turns.Where(t => t.Role == TranscriptTurn.CandidateRole).Select(t => t.Message).ToArray());
        Assert.Equal(new[] { "Opening problem.", "PROBE 1", "NEXT PROBLEM INTRO" },
            turns.Where(t => t.Role == TranscriptTurn.InterviewerRole).Select(t => t.Message).ToArray());

        // The intro turn carries the new problem id so /complete can map verdicts (#148).
        Assert.Equal(sender.Next.QuestionId, turns[^1].QuestionId);

        // Selector was asked for every problem already in the transcript, never a repeat.
        var openingId = turns[0].QuestionId!.Value;
        Assert.Equal(new[] { openingId }, sender.LastExcluded!.ToArray());
    }

    [Fact]
    public async Task Answer_SolvedVerdict_AdvancesOnFirstAnswer()
    {
        var (db, handler, generator, sender) = CreateHandler();
        generator.Solved = true;
        var interview = await SeedInterviewAsync(db);

        var result = await handler.Handle(new AnswerMockInterviewCommand
        {
            InterviewId = interview.Id,
            Message = "Kadane's algorithm: track the running max, O(n) time, O(1) space"
        }, CancellationToken.None);

        Assert.Equal(3, result.Data!.TurnNumber); // opening + candidate + intro — no probe
        Assert.Equal("NEXT PROBLEM INTRO", result.Data.InterviewerMessage);
        Assert.True(result.Data.InterviewOngoing);
        Assert.Equal(sender.Next.QuestionId, result.Data.QuestionId);

        var stored = await db.MockInterviews.IgnoreQueryFilters().SingleAsync(m => m.Id == interview.Id);
        Assert.Equal(1, stored.AnsweredQuestions);
        Assert.Equal(sender.Next.QuestionId, ReadTranscript(stored)[^1].QuestionId);
    }

    [Fact]
    public async Task Answer_ModelUnavailable_ProbesThenBudgetAdvances_NoStall()
    {
        var (db, handler, generator, _) = CreateHandler();
        generator.Solved = null; // model down: verdict unavailable
        generator.FollowUp = "PROBE";
        var interview = await SeedInterviewAsync(db);

        var first = await handler.Handle(new AnswerMockInterviewCommand
        {
            InterviewId = interview.Id,
            Message = "answer one"
        }, CancellationToken.None);

        Assert.Equal("PROBE", first.Data!.InterviewerMessage);
        Assert.True(first.Data.InterviewOngoing);

        var second = await handler.Handle(new AnswerMockInterviewCommand
        {
            InterviewId = interview.Id,
            Message = "answer two"
        }, CancellationToken.None);

        Assert.Equal("NEXT PROBLEM INTRO", second.Data!.InterviewerMessage);
        Assert.True(second.Data.InterviewOngoing);

        var stored = await db.MockInterviews.IgnoreQueryFilters().SingleAsync(m => m.Id == interview.Id);
        Assert.Equal(1, stored.AnsweredQuestions);
    }

    [Fact]
    public async Task Answer_FinalProblemAnswered_ReturnsOngoingFalseAndClosingLine()
    {
        var (db, handler, generator, _) = CreateHandler();
        generator.Solved = true;
        var interview = await SeedInterviewAsync(db, answeredQuestions: 2);

        var result = await handler.Handle(new AnswerMockInterviewCommand
        {
            InterviewId = interview.Id,
            Message = "final answer"
        }, CancellationToken.None);

        Assert.False(result.Data!.InterviewOngoing);
        Assert.Null(result.Data.QuestionId);
        Assert.Contains("finish the interview", result.Data.InterviewerMessage, StringComparison.OrdinalIgnoreCase);

        var stored = await db.MockInterviews.IgnoreQueryFilters().SingleAsync(m => m.Id == interview.Id);
        Assert.Equal(3, stored.AnsweredQuestions);
        Assert.Equal(MockInterviewStatus.InProgress, stored.Status); // /complete still owns the terminal flip
    }

    [Fact]
    public async Task Answer_AllProblemsAnswered_ThrowsConflict()
    {
        var (db, handler, _, _) = CreateHandler();
        var interview = await SeedInterviewAsync(db, answeredQuestions: 3);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new AnswerMockInterviewCommand
            {
                InterviewId = interview.Id,
                Message = "one more"
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Answer_GeneratorUnavailable_UsesCannedProbe()
    {
        var (db, handler, generator, _) = CreateHandler();
        generator.FollowUp = null;
        var interview = await SeedInterviewAsync(db);

        var result = await handler.Handle(new AnswerMockInterviewCommand
        {
            InterviewId = interview.Id,
            Message = "Kadane's algorithm"
        }, CancellationToken.None);

        Assert.NotNull(result.Data);
        Assert.Contains("complexity", result.Data!.InterviewerMessage, StringComparison.OrdinalIgnoreCase);

        var stored = await db.MockInterviews.IgnoreQueryFilters().SingleAsync(m => m.Id == interview.Id);
        var turns = ReadTranscript(stored);
        Assert.Equal(3, turns.Count);
        Assert.Equal(TranscriptTurn.CandidateRole, turns[1].Role);
        Assert.Equal("Kadane's algorithm", turns[1].Message);
        Assert.Equal(TranscriptTurn.InterviewerRole, turns[2].Role);
        Assert.Equal(0, stored.AnsweredQuestions); // degraded probe still does not advance
    }
}
