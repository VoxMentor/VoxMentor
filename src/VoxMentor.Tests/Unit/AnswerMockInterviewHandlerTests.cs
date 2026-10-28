using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Services;
using VoxMentor.Application.Features.MockInterviews;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="AnswerMockInterviewHandler"/>: 404 for unknown and
/// foreign interviews, 409 once ended, transcript growth with order preserved,
/// and the canned probe when Ollama is unavailable (#124).
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
            if (FollowUp is not null)
            {
                yield return new TutorChunk(FollowUp);
            }
        }

        public Task<MockInterviewReviewDraft?> ReviewAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            InterviewType type,
            string? company,
            string? role,
            CancellationToken cancellationToken = default)
            => Task.FromResult<MockInterviewReviewDraft?>(null);
    }

    private static (ApplicationDbContext Db, AnswerMockInterviewHandler Handler, StubGenerator Generator) CreateHandler(
        string userId = "user-1")
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeCurrentUserService { UserId = userId });

        var generator = new StubGenerator();
        var session = new MockInterviewSessionService(
            db, new FakeCurrentUserService { UserId = userId },
            sender: null!, generator, configuration: null!, new AsyncKeyedLock());
        return (db, new AnswerMockInterviewHandler(session), generator);
    }

    private static async Task<MockInterview> SeedInterviewAsync(
        ApplicationDbContext db,
        string userId = "user-1",
        MockInterviewStatus status = MockInterviewStatus.InProgress)
    {
        var interview = new MockInterview
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = InterviewType.Technical,
            TotalQuestions = 3,
            Status = status,
            StartedAt = DateTime.UtcNow,
            TranscriptJson = JsonSerializer.Serialize(
                new List<TranscriptTurn>
                {
                    new(TranscriptTurn.InterviewerRole, "Opening problem.", Guid.NewGuid(), DateTime.UtcNow)
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
        var (db, handler, _) = CreateHandler();

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
        var (db, handler, _) = CreateHandler();
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
        var (db, handler, _) = CreateHandler();
        var interview = await SeedInterviewAsync(db, status: MockInterviewStatus.Completed);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new AnswerMockInterviewCommand
            {
                InterviewId = interview.Id,
                Message = "my answer"
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Answer_AppendsTwoTurnsPerAnswer_PreservesOrder_AndCountsTurns()
    {
        var (db, handler, generator) = CreateHandler();
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

        generator.FollowUp = "PROBE 2";
        var second = await handler.Handle(new AnswerMockInterviewCommand
        {
            InterviewId = interview.Id,
            Message = "answer two"
        }, CancellationToken.None);

        Assert.Equal(5, second.Data!.TurnNumber);
        Assert.Equal("PROBE 2", second.Data.InterviewerMessage);

        var stored = await db.MockInterviews.IgnoreQueryFilters().SingleAsync(m => m.Id == interview.Id);
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
        Assert.Equal(new[] { "PROBE 1", "PROBE 2" },
            turns.Where(t => t.Role == TranscriptTurn.InterviewerRole && t.Message != "Opening problem.")
                .Select(t => t.Message).ToArray());
    }

    [Fact]
    public async Task Answer_GeneratorUnavailable_UsesCannedProbe()
    {
        var (db, handler, generator) = CreateHandler();
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
    }
}
