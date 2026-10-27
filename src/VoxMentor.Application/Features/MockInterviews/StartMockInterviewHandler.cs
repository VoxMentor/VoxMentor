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

/// <summary>
/// POST /api/v1/mock/start (#124): picks the opening problem through the
/// practice selector (JD-weighted ε-greedy from #119), asks Ollama for the
/// opening line, enforces the per-student daily limit (429), and persists an
/// InProgress interview whose TranscriptJson starts with the interviewer turn.
/// </summary>
public class StartMockInterviewHandler : IRequestHandler<StartMockInterviewCommand, ApiResponse<StartMockInterviewResultDto>>
{
    /// <summary>Planned problem count per run (API.md §6 caps it at 3).</summary>
    private const int TotalQuestions = 3;

    /// <summary>Response duration in minutes, per the documented 202 body.</summary>
    private const int DurationMinutes = 45;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _sender;
    private readonly IMockInterviewGenerator _generator;
    private readonly IConfiguration _configuration;
    private readonly AsyncKeyedLock _lock;
    private readonly TimeProvider _clock;

    /// <summary>Initializes the handler with the data context, user, selector sender, generator, config, and lock.</summary>
    public StartMockInterviewHandler(
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

    /// <summary>Validates the JD, applies the daily limit, selects the opening problem, and starts the run.</summary>
    public async Task<ApiResponse<StartMockInterviewResultDto>> Handle(
        StartMockInterviewCommand request, CancellationToken cancellationToken)
    {
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
        // same window cannot both pass and a losing start fails the re-count
        // before any generation call.
        using (await _lock.AcquireAsync($"mock:start:{userId}", cancellationToken))
        {
            // One clock reading under the lock feeds both the quota day and
            // StartedAt: a generation window crossing UTC midnight counts and
            // stamps the same day (CodeRabbit #146).
            var now = _clock.GetUtcNow().UtcDateTime;
            var todayUtc = now.Date;
            var tomorrowUtc = todayUtc.AddDays(1);
            if (await CountStartedTodayAsync(todayUtc, tomorrowUtc, cancellationToken) >= dailyLimit)
                throw new RateLimitException(RetryAfterSeconds(tomorrowUtc));

            var next = await _sender.Send(new GetNextQuestionQuery { JdId = request.JdId }, cancellationToken);
            var problem = next.Data
                ?? throw new NotFoundException("No questions available for an interview.");

            var fallbackOpening = BuildFallbackOpening(company, role, problem.Title);
            var openingMessage = await _generator.OpeningAsync(company, role, problem.Title, cancellationToken)
                ?? fallbackOpening;

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

            return ApiResponse<StartMockInterviewResultDto>.SuccessResult(new StartMockInterviewResultDto(
                interview.Id,
                problem.QuestionId,
                interview.Type.ToString(),
                interview.Status.ToString(),
                DurationMinutes,
                interview.StartedAt,
                openingMessage));
        }
    }

    /// <summary>Interviews started by the current student within the given UTC day (the IUserOwned global filter scopes every MockInterviews query to them).</summary>
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
}
