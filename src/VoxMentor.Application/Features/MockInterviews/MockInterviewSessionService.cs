using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Services;
using VoxMentor.Application.Features.Practice.GetNextQuestion;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>One streamed unit of a mock-interview start: a text token, or the final result.</summary>
public sealed record StartMockInterviewChunk(string? Token, StartMockInterviewResultDto? Result = null);

/// <summary>One streamed unit of a mock-interview answer: a text token, or the final result.</summary>
public sealed record AnswerMockInterviewChunk(string? Token, AnswerMockInterviewResultDto? Result = null);

/// <summary>
/// The single implementation of the mock-interview dialogue flow (#126): the
/// buffered REST handlers (start/answer) aggregate these streams — degrading
/// to a canned line when generation fails, the #124 never-500 contract — and
/// InterviewHub forwards the same chunks to the client as they are generated.
/// Complete/review stays on CompleteMockInterviewCommand (no streaming).
/// </summary>
public class MockInterviewSessionService
{
    /// <summary>Planned problem count per run (API.md §6 caps it at 3).</summary>
    private const int TotalQuestions = 3;

    /// <summary>Response duration in minutes, per the documented 202 body.</summary>
    private const int DurationMinutes = 45;

    /// <summary>Canned probe used when Ollama is unreachable — degrade, never 500.</summary>
    private const string CannedFollowUp =
        "Good. What's the time and space complexity of your approach, and which edge cases break it?";

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _sender;
    private readonly IMockInterviewGenerator _generator;
    private readonly IConfiguration _configuration;
    private readonly AsyncKeyedLock _lock;

    /// <summary>Initializes the service with the data context, user, selector sender, generator, config, and lock.</summary>
    public MockInterviewSessionService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ISender sender,
        IMockInterviewGenerator generator,
        IConfiguration configuration,
        AsyncKeyedLock @lock)
    {
        _db = db;
        _currentUser = currentUser;
        _sender = sender;
        _generator = generator;
        _configuration = configuration;
        _lock = @lock;
    }

    /// <summary>Buffered start for REST: aggregate the stream, degrading to the canned opener when generation fails.</summary>
    public async Task<StartMockInterviewResultDto> StartAsync(
        StartMockInterviewCommand request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await FinalizeStartAsync(StreamStartAsync(request, cancellationToken: cancellationToken), cancellationToken);
        }
        catch (MockInterviewGenerationException)
        {
            return await FinalizeStartAsync(
                StreamStartAsync(request, CannedDialogueGenerator.Instance, cancellationToken), cancellationToken);
        }
    }

    /// <summary>Buffered answer for REST: aggregate the stream, degrading to the canned probe when generation fails.</summary>
    public async Task<AnswerMockInterviewResultDto> AnswerAsync(
        AnswerMockInterviewCommand request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await FinalizeAnswerAsync(StreamAnswerAsync(request, cancellationToken: cancellationToken), cancellationToken);
        }
        catch (MockInterviewGenerationException)
        {
            return await FinalizeAnswerAsync(
                StreamAnswerAsync(request, CannedDialogueGenerator.Instance, cancellationToken), cancellationToken);
        }
    }

    /// <summary>
    /// Validates the JD, applies the daily limit, selects the opening problem,
    /// and streams the opening tokens; the final chunk carries the start result.
    /// Throws UnauthorizedAccessException/NotFoundException/RateLimitException
    /// or MockInterviewGenerationException — the buffered callers degrade on
    /// the latter, the hub emits InterviewError on any of them.
    /// </summary>
    public async IAsyncEnumerable<StartMockInterviewChunk> StreamStartAsync(
        StartMockInterviewCommand request,
        IMockInterviewGenerator? generatorOverride = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var generator = generatorOverride ?? _generator;

        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User must be authenticated to start a mock interview.");

        string? company = null;
        string? role = null;
        if (request.JdId is { } jdId)
        {
            // IUserOwned global filter → unknown or another student's JD is null → 404.
            var jd = await _db.JobDescriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jdId, cancellationToken)
                ?? throw new NotFoundException($"Job description {jdId} was not found.");
            company = jd.CompanyName;
            role = jd.Role;
        }

        // Advisory fast path: reject over-quota requests before the slow Ollama
        // call; the authoritative limit check is the locked re-count below
        // (CodeRabbit #146: count-then-insert alone lets two parallel starts pass).
        var todayUtc = DateTime.UtcNow.Date;
        var tomorrowUtc = todayUtc.AddDays(1);
        var dailyLimit = ReadDailyLimit();
        if (await CountStartedTodayAsync(todayUtc, tomorrowUtc, cancellationToken) >= dailyLimit)
            throw new RateLimitException(RetryAfterSeconds(todayUtc, tomorrowUtc));

        // Call through the practice selector (#124): same ε-greedy path, pinned
        // to the interview's JD weights when one was supplied.
        var next = await _sender.Send(new GetNextQuestionQuery { JdId = request.JdId }, cancellationToken);
        var problem = next.Data
            ?? throw new NotFoundException("No questions available for an interview.");

        var opening = new StringBuilder();
        await foreach (var chunk in generator.OpeningStreamAsync(company, role, problem.Title, cancellationToken))
        {
            if (chunk.Text.Length == 0)
                continue;
            opening.Append(chunk.Text);
            yield return new StartMockInterviewChunk(chunk.Text);
        }
        if (opening.Length == 0)
            throw new MockInterviewGenerationException("The AI interviewer returned an empty opening.");

        var openingMessage = opening.ToString();
        var interview = new MockInterview
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = ParseType(request.Type),
            TotalQuestions = TotalQuestions,
            AnsweredQuestions = 0,
            StartedAt = DateTime.UtcNow,
            JdId = request.JdId,
            Status = MockInterviewStatus.InProgress,
            TranscriptJson = JsonSerializer.Serialize(
                new List<TranscriptTurn>
                {
                    new(TranscriptTurn.InterviewerRole, openingMessage, problem.QuestionId, DateTime.UtcNow)
                },
                TranscriptTurn.JsonOptions)
        };

        // #124 concurrency (CodeRabbit #146): the fast-path count is advisory —
        // this per-student lock serializes the authoritative re-count and the
        // insert, so two starts in the same generation window cannot both pass.
        using (await _lock.AcquireAsync($"mock:start:{userId}", cancellationToken))
        {
            if (await CountStartedTodayAsync(todayUtc, tomorrowUtc, cancellationToken) >= dailyLimit)
                throw new RateLimitException(RetryAfterSeconds(todayUtc, tomorrowUtc));

            _db.MockInterviews.Add(interview);
            await _db.SaveChangesAsync(cancellationToken);
        }

        yield return new StartMockInterviewChunk(null, new StartMockInterviewResultDto(
            interview.Id,
            problem.QuestionId,
            interview.Type.ToString(),
            interview.Status.ToString(),
            DurationMinutes,
            interview.StartedAt,
            openingMessage));
    }

    /// <summary>
    /// Appends the candidate's turn, streams the interviewer's probe under the
    /// per-interview lock, then persists both turns; the final chunk carries
    /// the answer result. 404 for unknown/foreign ids (IUserOwned filter),
    /// 409 once the run has ended.
    /// </summary>
    public async IAsyncEnumerable<AnswerMockInterviewChunk> StreamAnswerAsync(
        AnswerMockInterviewCommand request,
        IMockInterviewGenerator? generatorOverride = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var generator = generatorOverride ?? _generator;

        // #124/#125 concurrency (CodeRabbit #146): serialize ALL writes on one
        // interview — answers and the complete/review pass — so the
        // read-append-write below cannot drop a turn and complete cannot
        // interleave with a late answer. In-process ceiling documented on
        // AsyncKeyedLock.
        using (await _lock.AcquireAsync($"mock:interview:{request.InterviewId}", cancellationToken))
        {
            var interview = await _db.MockInterviews
                .FirstOrDefaultAsync(m => m.Id == request.InterviewId, cancellationToken)
                ?? throw new NotFoundException("Interview not found.");

            if (interview.Status is MockInterviewStatus.Completed or MockInterviewStatus.Failed)
                throw new ConflictException("Interview has already ended.");

            var turns = string.IsNullOrEmpty(interview.TranscriptJson)
                ? new List<TranscriptTurn>()
                : JsonSerializer.Deserialize<List<TranscriptTurn>>(interview.TranscriptJson, TranscriptTurn.JsonOptions)
                  ?? new List<TranscriptTurn>();

            turns.Add(new TranscriptTurn(TranscriptTurn.CandidateRole, request.Message, null, DateTime.UtcNow));

            var probe = new StringBuilder();
            await foreach (var chunk in generator.FollowUpStreamAsync(turns, cancellationToken))
            {
                if (chunk.Text.Length == 0)
                    continue;
                probe.Append(chunk.Text);
                yield return new AnswerMockInterviewChunk(chunk.Text);
            }
            if (probe.Length == 0)
                throw new MockInterviewGenerationException("The AI interviewer returned an empty follow-up.");

            var followUp = probe.ToString();
            turns.Add(new TranscriptTurn(TranscriptTurn.InterviewerRole, followUp, null, DateTime.UtcNow));

            interview.TranscriptJson = JsonSerializer.Serialize(turns, TranscriptTurn.JsonOptions);
            await _db.SaveChangesAsync(cancellationToken);

            yield return new AnswerMockInterviewChunk(null, new AnswerMockInterviewResultDto(
                followUp, turns.Count, InterviewOngoing: true));
        }
    }

    /// <summary>Drains a start stream and returns the final result chunk.</summary>
    private static async Task<StartMockInterviewResultDto> FinalizeStartAsync(
        IAsyncEnumerable<StartMockInterviewChunk> chunks, CancellationToken cancellationToken)
    {
        StartMockInterviewResultDto? result = null;
        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            if (chunk.Result is { } r)
                result = r;
        }
        return result ?? throw new MockInterviewGenerationException("The interview ended before a result was produced.");
    }

    /// <summary>Drains an answer stream and returns the final result chunk.</summary>
    private static async Task<AnswerMockInterviewResultDto> FinalizeAnswerAsync(
        IAsyncEnumerable<AnswerMockInterviewChunk> chunks, CancellationToken cancellationToken)
    {
        AnswerMockInterviewResultDto? result = null;
        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            if (chunk.Result is { } r)
                result = r;
        }
        return result ?? throw new MockInterviewGenerationException("The interview ended before a result was produced.");
    }

    /// <summary>Interviews started by any student within the given UTC day (the IUserOwned global filter scopes every MockInterviews query to them).</summary>
    private Task<int> CountStartedTodayAsync(DateTime todayUtc, DateTime tomorrowUtc, CancellationToken cancellationToken)
        => _db.MockInterviews
            .CountAsync(m => m.StartedAt >= todayUtc && m.StartedAt < tomorrowUtc, cancellationToken);

    /// <summary>Seconds until UTC midnight, when the daily quota resets.</summary>
    private static int RetryAfterSeconds(DateTime todayUtc, DateTime tomorrowUtc)
        => (int)Math.Ceiling((tomorrowUtc - DateTime.UtcNow).TotalSeconds);

    /// <summary>Configurable daily quota (MockInterview:DailyLimit); defaults to 1, the free-tier value.</summary>
    private int ReadDailyLimit()
        => int.TryParse(_configuration["MockInterview:DailyLimit"], out var limit) && limit > 0
            ? limit
            : 1;

    /// <summary>Canned opener used when Ollama is unreachable — degrade, never 500.</summary>
    private static string BuildFallbackOpening(string? company, string? role, string problemTitle)
    {
        var greeting = company is not null && role is not null
            ? $"Hi, I'm your AI interviewer at {company} for the {role} position. "
            : "Hi, I'm your AI interviewer today. ";
        return $"{greeting}Let's start with your first problem: {problemTitle}.";
    }

    /// <summary>Case-insensitive enum parse; anything not a named defined value → Technical (the validator rejects the rest).</summary>
    private static InterviewType ParseType(string? type)
        => !string.IsNullOrWhiteSpace(type) &&
           Enum.TryParse<InterviewType>(type.Trim(), ignoreCase: true, out var parsed) &&
           Enum.IsDefined(parsed)
            ? parsed
            : InterviewType.Technical;

    /// <summary>
    /// Deterministic dialogue for the REST canned fallback when Ollama fails
    /// (#124 degrade contract). Never throws.
    /// </summary>
    private sealed class CannedDialogueGenerator : IMockInterviewGenerator
    {
        public static CannedDialogueGenerator Instance { get; } = new();

        public async IAsyncEnumerable<TutorChunk> OpeningStreamAsync(
            string? company, string? role, string problemTitle,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask; // async-iterator shape; canned text has nothing to await
            yield return new TutorChunk(BuildFallbackOpening(company, role, problemTitle));
        }

        public async IAsyncEnumerable<TutorChunk> FollowUpStreamAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new TutorChunk(CannedFollowUp);
        }

        public Task<MockInterviewReviewDraft?> ReviewAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            InterviewType type,
            string? company,
            string? role,
            CancellationToken cancellationToken = default)
            => Task.FromResult<MockInterviewReviewDraft?>(null);
    }
}
