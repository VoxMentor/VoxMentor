using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
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
/// #148: an answer that satisfies the current problem (or spends the probe
/// budget) increments AnsweredQuestions and introduces the next problem, so
/// interviewOngoing is honest and /complete closes out over all problems.
/// </summary>
public class MockInterviewSessionService
{
    /// <summary>Planned problem count per run (API.md §6 caps it at 3).</summary>
    private const int TotalQuestions = 3;

    /// <summary>Probe turns allowed per problem before #148 advances regardless of the verdict.</summary>
    private const int ProbesPerProblem = 1;

    /// <summary>Response duration in minutes, per the documented 202 body.</summary>
    private const int DurationMinutes = 45;

    /// <summary>Canned probe used when Ollama is unreachable — degrade, never 500.</summary>
    private const string CannedFollowUp =
        "Good. What's the time and space complexity of your approach, and which edge cases break it?";

    /// <summary>Canned wrap-up once the final problem is answered (#148) — no generator call that could fail.</summary>
    private const string CannedClosing =
        "That's all the problems I have for you. Whenever you're ready, finish the interview to get your review.";

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _sender;
    private readonly IMockInterviewGenerator _generator;
    private readonly IConfiguration _configuration;
    private readonly AsyncKeyedLock _lock;
    private readonly TimeProvider _clock;

    /// <summary>Initializes the service with the data context, user, selector sender, generator, config, and lock.</summary>
    public MockInterviewSessionService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ISender sender,
        IMockInterviewGenerator generator,
        IConfiguration configuration,
        AsyncKeyedLock @lock,
        TimeProvider? clock = null)
    {
        _db = db;
        _currentUser = currentUser;
        _sender = sender;
        _generator = generator;
        _configuration = configuration;
        _lock = @lock;
        _clock = clock ?? TimeProvider.System;
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

    /// <summary>
    /// Buffered answer for REST: aggregate the stream, degrading to the canned
    /// line when generation fails. The solved verdict rides along in a box so
    /// the retry — whose canned judge always answers null — cannot demote a
    /// satisfying answer to a probe instead of advancing it (#148).
    /// </summary>
    public async Task<AnswerMockInterviewResultDto> AnswerAsync(
        AnswerMockInterviewCommand request, CancellationToken cancellationToken = default)
    {
        var solvedAcrossRetry = new StrongBox<bool>();
        try
        {
            return await FinalizeAnswerAsync(
                StreamAnswerAsync(request, solvedAcrossRetry: solvedAcrossRetry, cancellationToken: cancellationToken),
                cancellationToken);
        }
        catch (MockInterviewGenerationException)
        {
            return await FinalizeAnswerAsync(
                StreamAnswerAsync(request, CannedDialogueGenerator.Instance, solvedAcrossRetry, cancellationToken),
                cancellationToken);
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
        // call; the authoritative check runs under the per-student lock below
        // (CodeRabbit #146: count-then-insert alone lets two parallel starts pass).
        var dailyLimit = ReadDailyLimit();
        var advisoryToday = _clock.GetUtcNow().UtcDateTime.Date;
        var advisoryTomorrow = advisoryToday.AddDays(1);
        if (await CountStartedTodayAsync(advisoryToday, advisoryTomorrow, cancellationToken) >= dailyLimit)
            throw new RateLimitException(RetryAfterSeconds(advisoryTomorrow));

        // #124 concurrency (CodeRabbit #146): the fast-path count is advisory —
        // this per-student lock serializes the authoritative re-count, problem
        // selection, opener generation, and the insert, so two starts in the
        // same generation window cannot both pass and a losing start fails the
        // re-count before any generation call. One clock reading under the
        // lock feeds both the quota day and StartedAt: a generation window
        // crossing UTC midnight counts and stamps the same day.
        using (await _lock.AcquireAsync($"mock:start:{userId}", cancellationToken))
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var todayUtc = now.Date;
            var tomorrowUtc = todayUtc.AddDays(1);
            if (await CountStartedTodayAsync(todayUtc, tomorrowUtc, cancellationToken) >= dailyLimit)
                throw new RateLimitException(RetryAfterSeconds(tomorrowUtc));

            // Call through the practice selector (#124): same ε-greedy path,
            // pinned to the interview's JD weights when one was supplied.
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
                StartedAt = now,
                JdId = request.JdId,
                Status = MockInterviewStatus.InProgress,
                TranscriptJson = JsonSerializer.Serialize(
                    new List<TranscriptTurn>
                    {
                        new(TranscriptTurn.InterviewerRole, openingMessage, problem.QuestionId, now)
                    },
                    TranscriptTurn.JsonOptions)
            };

            _db.MockInterviews.Add(interview);
            await _db.SaveChangesAsync(cancellationToken);

            yield return new StartMockInterviewChunk(null, new StartMockInterviewResultDto(
                interview.Id,
                problem.QuestionId,
                interview.Type.ToString(),
                interview.Status.ToString(),
                DurationMinutes,
                interview.StartedAt,
                openingMessage));
        }
    }

    /// <summary>
    /// Appends the candidate's turn, then either advances the interview (#148:
    /// satisfied verdict or spent probe budget → AnsweredQuestions++, select
    /// and introduce the next problem, capped at TotalQuestions) or streams one
    /// probe — all under the per-interview lock; the final chunk carries an
    /// honest interviewOngoing and the problem id the interview is now on.
    /// 404 for unknown/foreign ids (IUserOwned filter), 409 once the run has
    /// ended — including after the last problem was answered. Every generator
    /// call runs BEFORE any entity mutation, so the canned retry of
    /// AnswerAsync re-runs against a clean unit of work — carrying the verdict
    /// already reached in solvedAcrossRetry (null on the hub path, which never
    /// retries) so a solved answer cannot be re-judged as a probe.
    /// </summary>
    public async IAsyncEnumerable<AnswerMockInterviewChunk> StreamAnswerAsync(
        AnswerMockInterviewCommand request,
        IMockInterviewGenerator? generatorOverride = null,
        StrongBox<bool>? solvedAcrossRetry = null,
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

            // #148: all problems answered (the client already saw
            // interviewOngoing=false) — only /complete may close the run now.
            if (interview.AnsweredQuestions >= interview.TotalQuestions)
                throw new ConflictException("Interview has already ended. Complete it to get your review.");

            var turns = string.IsNullOrEmpty(interview.TranscriptJson)
                ? new List<TranscriptTurn>()
                : JsonSerializer.Deserialize<List<TranscriptTurn>>(interview.TranscriptJson, TranscriptTurn.JsonOptions)
                  ?? new List<TranscriptTurn>();

            // Only intro turns carry a QuestionId, so the last one marks the
            // current problem; interviewer turns after it are probes on it.
            var currentIndex = turns.FindLastIndex(t => t.QuestionId.HasValue);
            var currentQuestionId = currentIndex >= 0 ? turns[currentIndex].QuestionId : null;
            var probesSpent = currentIndex >= 0
                ? turns.Skip(currentIndex + 1).Count(t => t.Role == TranscriptTurn.InterviewerRole)
                : 0;

            turns.Add(new TranscriptTurn(TranscriptTurn.CandidateRole, request.Message, null, DateTime.UtcNow));

            // #148: advance when the model is satisfied OR the probe budget is
            // spent (short-circuit: one verdict call per problem at most, and a
            // dead or contrary model can never strand the run on one problem).
            // The box carries an already-solved verdict across AnswerAsync's
            // canned retry, whose judge always answers null.
            var advance = solvedAcrossRetry is { Value: true }
                || probesSpent >= ProbesPerProblem
                || await generator.AssessAnswerSolvedAsync(turns, interview.Type, cancellationToken) == true;
            if (solvedAcrossRetry is not null)
            {
                solvedAcrossRetry.Value = advance;
            }
            var answeredAfter = advance ? interview.AnsweredQuestions + 1 : interview.AnsweredQuestions;

            string reply;
            Guid? replyQuestionId;
            if (!advance)
            {
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

                reply = probe.ToString();
                replyQuestionId = currentQuestionId;
                turns.Add(new TranscriptTurn(TranscriptTurn.InterviewerRole, reply, null, DateTime.UtcNow));
            }
            else if (answeredAfter >= interview.TotalQuestions)
            {
                // Final problem answered: canned closing keeps the degrade
                // contract — no Ollama call here can fail the request (#148).
                reply = CannedClosing;
                replyQuestionId = null;
                yield return new AnswerMockInterviewChunk(reply);
                turns.Add(new TranscriptTurn(TranscriptTurn.InterviewerRole, reply, currentQuestionId, DateTime.UtcNow));
            }
            else
            {
                var problem = await SelectNextProblemAsync(interview, turns, cancellationToken);

                string? company = null;
                string? role = null;
                if (interview.JdId is { } jdId)
                {
                    // IUserOwned global filter scopes this like complete does.
                    var jd = await _db.JobDescriptions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(j => j.Id == jdId, cancellationToken);
                    company = jd?.CompanyName;
                    role = jd?.Role;
                }

                var intro = new StringBuilder();
                await foreach (var chunk in generator.OpeningStreamAsync(company, role, problem.Title, cancellationToken))
                {
                    if (chunk.Text.Length == 0)
                        continue;
                    intro.Append(chunk.Text);
                    yield return new AnswerMockInterviewChunk(chunk.Text);
                }
                if (intro.Length == 0)
                    throw new MockInterviewGenerationException("The AI interviewer returned an empty opening.");

                reply = intro.ToString();
                replyQuestionId = problem.QuestionId;
                turns.Add(new TranscriptTurn(TranscriptTurn.InterviewerRole, reply, problem.QuestionId, DateTime.UtcNow));
            }

            // Every generation call above succeeded — only now mutate and save.
            interview.AnsweredQuestions = answeredAfter;
            interview.TranscriptJson = JsonSerializer.Serialize(turns, TranscriptTurn.JsonOptions);
            await _db.SaveChangesAsync(cancellationToken);

            yield return new AnswerMockInterviewChunk(null, new AnswerMockInterviewResultDto(
                reply,
                turns.Count,
                InterviewOngoing: answeredAfter < interview.TotalQuestions,
                QuestionId: replyQuestionId));
        }
    }

    /// <summary>
    /// Next problem for a running interview: the adaptive selector minus every
    /// problem already asked (#148 — no repeats within a run). When the pool
    /// holds fewer distinct questions than TotalQuestions, the retry without
    /// exclusions may repeat one rather than failing the run
    /// (ponytail: a repeat beats a mid-interview 404).
    /// </summary>
    private async Task<NextQuestionDto> SelectNextProblemAsync(
        MockInterview interview,
        IReadOnlyList<TranscriptTurn> turns,
        CancellationToken cancellationToken)
    {
        var asked = turns
            .Where(t => t.QuestionId.HasValue)
            .Select(t => t.QuestionId!.Value)
            .Distinct()
            .ToList();

        ApiResponse<NextQuestionDto> next;
        try
        {
            next = await _sender.Send(new GetNextQuestionQuery
            {
                JdId = interview.JdId,
                ExcludeQuestionIds = asked
            }, cancellationToken);
        }
        catch (NotFoundException)
        {
            next = await _sender.Send(new GetNextQuestionQuery { JdId = interview.JdId }, cancellationToken);
        }

        return next.Data ?? throw new NotFoundException("No questions available for an interview.");
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
    private int RetryAfterSeconds(DateTime tomorrowUtc)
        => (int)Math.Ceiling((tomorrowUtc - _clock.GetUtcNow().UtcDateTime).TotalSeconds);

    /// <summary>Configurable daily quota (MockInterview:DailyLimit); defaults to 1, the free-tier value.</summary>
    private int ReadDailyLimit()
        => int.TryParse(_configuration["MockInterview:DailyLimit"], out var limit) && limit > 0
            ? limit
            : 1;

    /// <summary>Canned problem intro used when Ollama is unreachable — degrade, never 500. Worded for both the opening and a #148 mid-run advance.</summary>
    private static string BuildFallbackOpening(string? company, string? role, string problemTitle)
    {
        var greeting = company is not null && role is not null
            ? $"Hi, I'm your AI interviewer at {company} for the {role} position. "
            : "Hi, I'm your AI interviewer today. ";
        return $"{greeting}Let's take on this problem: {problemTitle}.";
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

        // Canned dialogue never judges → null → the probe budget governs (#148).
        public Task<bool?> AssessAnswerSolvedAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            InterviewType type,
            CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(null);

        public Task<MockInterviewReviewDraft?> ReviewAsync(
            IReadOnlyList<TranscriptTurn> transcript,
            InterviewType type,
            string? company,
            string? role,
            CancellationToken cancellationToken = default)
            => Task.FromResult<MockInterviewReviewDraft?>(null);
    }
}
