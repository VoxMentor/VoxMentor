using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Practice.GetNextQuestion;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>
/// POST /api/v1/mock/start (#124): picks the opening problem through the
/// practice selector (JD-weighted Îµ-greedy from #119), asks Ollama for the
/// opening line, enforces the per-student daily limit (429), and persists an
/// InProgress interview whose TranscriptJson starts with the interviewer turn.
/// </summary>
public class StartMockInterviewHandler : IRequestHandler<StartMockInterviewCommand, ApiResponse<StartMockInterviewResultDto>>
{
    /// <summary>Planned problem count per run (API.md Â§6 caps it at 3).</summary>
    private const int TotalQuestions = 3;

    /// <summary>Response duration in minutes, per the documented 202 body.</summary>
    private const int DurationMinutes = 45;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _sender;
    private readonly IMockInterviewGenerator _generator;
    private readonly IConfiguration _configuration;

    /// <summary>Initializes the handler with the data context, user, selector sender, generator, and config.</summary>
    public StartMockInterviewHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ISender sender,
        IMockInterviewGenerator generator,
        IConfiguration configuration)
    {
        _db = db;
        _currentUser = currentUser;
        _sender = sender;
        _generator = generator;
        _configuration = configuration;
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
            // IUserOwned global filter â†’ unknown or another student's JD is null â†’ 404.
            var jd = await _db.JobDescriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jdId, cancellationToken)
                ?? throw new NotFoundException($"Job description {jdId} was not found.");
            company = jd.CompanyName;
            role = jd.Role;
        }

        // ponytail: count-then-insert on MockInterviews â€” two parallel starts can
        // both pass. Per-user atomic Lua (RedisSlidingWindowRateLimiter keyed
        // per day) is the upgrade if double-starts ever matter.
        var todayUtc = DateTime.UtcNow.Date;
        var tomorrowUtc = todayUtc.AddDays(1);
        var startedToday = await _db.MockInterviews
            .CountAsync(m => m.StartedAt >= todayUtc && m.StartedAt < tomorrowUtc, cancellationToken);
        var dailyLimit = ReadDailyLimit();
        if (startedToday >= dailyLimit)
        {
            var retryAfter = (int)Math.Ceiling((tomorrowUtc - DateTime.UtcNow).TotalSeconds);
            throw new RateLimitException(retryAfter);
        }

        // Call through the practice selector (#124): same Îµ-greedy path, pinned
        // to the interview's JD weights when one was supplied.
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

        _db.MockInterviews.Add(interview);
        await _db.SaveChangesAsync(cancellationToken);

        return ApiResponse<StartMockInterviewResultDto>.SuccessResult(new StartMockInterviewResultDto(
            interview.Id,
            interview.Type.ToString(),
            interview.Status.ToString(),
            DurationMinutes,
            interview.StartedAt,
            openingMessage));
    }

    /// <summary>Configurable daily quota (MockInterview:DailyLimit); defaults to 1, the free-tier value.</summary>
    private int ReadDailyLimit()
        => int.TryParse(_configuration["MockInterview:DailyLimit"], out var limit) && limit > 0
            ? limit
            : 1;

    /// <summary>Canned opener used when Ollama is unreachable â€” degrade, never 500.</summary>
    private static string BuildFallbackOpening(string? company, string? role, string problemTitle)
    {
        var greeting = company is not null && role is not null
            ? $"Hi, I'm your AI interviewer at {company} for the {role} position. "
            : "Hi, I'm your AI interviewer today. ";
        return $"{greeting}Let's start with your first problem: {problemTitle}.";
    }

    /// <summary>Case-insensitive enum parse; blank â†’ Technical (validator rejects the rest).</summary>
    private static InterviewType ParseType(string? type)
        => !string.IsNullOrWhiteSpace(type) && Enum.TryParse<InterviewType>(type, ignoreCase: true, out var parsed)
            ? parsed
            : InterviewType.Technical;
}
