using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.MockInterviews;
using ValidationException = VoxMentor.Application.Common.Exceptions.ValidationException;

namespace VoxMentor.Api.Hubs;

/// <summary>
/// Mock-interview streaming hub (#126). StartMock and SendAnswer run the same
/// <see cref="MockInterviewSessionService"/> dialogue as the REST endpoints:
/// interviewer tokens are forwarded as InterviewerMessage events as they are
/// generated, the invoke return carries the same DTOs the REST handlers wrap,
/// and every failure surfaces as InterviewError so the client is never left
/// hanging. CompleteMock routes CompleteMockInterviewCommand and emits
/// InterviewComplete with the real score only when the run actually ends.
/// </summary>
[Authorize(Roles = "Student")]
public class InterviewHub : Hub
{
    private readonly MockInterviewSessionService _session;
    private readonly ISender _sender;
    private readonly ICurrentUserService _currentUser;
    private readonly IApplicationDbContext _db;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<InterviewHub> _logger;
    private readonly IValidator<StartMockInterviewCommand> _startValidator;
    private readonly IValidator<AnswerMockInterviewCommand> _answerValidator;

    /// <summary>Initializes the hub with the session service, sender, user, data context, lifetime, logger, and validators.</summary>
    public InterviewHub(
        MockInterviewSessionService session,
        ISender sender,
        ICurrentUserService currentUser,
        IApplicationDbContext db,
        IHostApplicationLifetime lifetime,
        ILogger<InterviewHub> logger,
        IValidator<StartMockInterviewCommand> startValidator,
        IValidator<AnswerMockInterviewCommand> answerValidator)
    {
        _session = session;
        _sender = sender;
        _currentUser = currentUser;
        _db = db;
        _lifetime = lifetime;
        _logger = logger;
        _startValidator = startValidator;
        _answerValidator = answerValidator;
    }

    /// <summary>
    /// Starts a real interview, streams the opening tokens to the caller, and
    /// returns the same start DTO the REST 202 body carries (null on failure —
    /// the caller receives InterviewError instead).
    /// </summary>
    public async Task<StartMockInterviewResultDto?> StartMock(string? jdId, string? type, bool voiceMode)
    {
        // Link host shutdown with the client disconnect (#90 pattern): the
        // OperationCanceledException paths below must fire while the DbContext
        // is still alive, before Kestrel drains connections.
        using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(
            Context.ConnectionAborted, _lifetime.ApplicationStopping);
        var cancellationToken = shutdownCts.Token;

        Guid? parsedJdId = null;
        if (!string.IsNullOrWhiteSpace(jdId))
        {
            if (!Guid.TryParse(jdId, out var id))
            {
                await SendError(null, $"Invalid jdId '{jdId}'.");
                return null;
            }
            parsedJdId = id;
        }

        var command = new StartMockInterviewCommand
        {
            JdId = parsedJdId,
            Type = type,
            VoiceMode = voiceMode
        };

        var validation = await _startValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            await SendError(null, string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
            return null;
        }

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            await SendError(null, "User must be authenticated to start a mock interview.");
            return null;
        }

        try
        {
            StartMockInterviewResultDto? result = null;
            await foreach (var chunk in _session.StreamStartAsync(command, cancellationToken: cancellationToken))
            {
                if (chunk.Token is { Length: > 0 } token)
                    await Clients.Caller.SendAsync("InterviewerMessage", token, cancellationToken);
                if (chunk.Result is { } r)
                    result = r;
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            // Caller disconnected or host is shutting down — nothing to notify.
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InterviewHub StartMock failed");
            await SendError(null, DescribeError(ex));
            return null;
        }
    }

    /// <summary>
    /// Answers the current turn, streams the interviewer's probe tokens, and
    /// returns the same answer DTO the REST 200 body carries (null on failure).
    /// </summary>
    public async Task<AnswerMockInterviewResultDto?> SendAnswer(string? interviewId, string? message)
    {
        using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(
            Context.ConnectionAborted, _lifetime.ApplicationStopping);
        var cancellationToken = shutdownCts.Token;

        if (!Guid.TryParse(interviewId, out var id))
        {
            await SendError(null, $"Invalid interviewId '{interviewId}'.");
            return null;
        }

        var command = new AnswerMockInterviewCommand
        {
            InterviewId = id,
            Message = message?.Trim() ?? string.Empty
        };

        var validation = await _answerValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            await SendError(id, string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
            return null;
        }

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            await SendError(id, "User must be authenticated to answer a mock interview.");
            return null;
        }

        try
        {
            AnswerMockInterviewResultDto? result = null;
            await foreach (var chunk in _session.StreamAnswerAsync(command, cancellationToken: cancellationToken))
            {
                if (chunk.Token is { Length: > 0 } token)
                    await Clients.Caller.SendAsync("InterviewerMessage", token, cancellationToken);
                if (chunk.Result is { } r)
                    result = r;
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            // Caller disconnected or host is shutting down — nothing to notify.
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InterviewHub SendAnswer failed");
            await SendError(id, DescribeError(ex));
            return null;
        }
    }

    /// <summary>Completes the interview and emits InterviewComplete with the real score.</summary>
    public async Task CompleteMock(string? interviewId)
    {
        using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(
            Context.ConnectionAborted, _lifetime.ApplicationStopping);
        var cancellationToken = shutdownCts.Token;

        if (!Guid.TryParse(interviewId, out var id))
        {
            await SendError(null, $"Invalid interviewId '{interviewId}'.");
            return;
        }

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            await SendError(id, "User must be authenticated to complete a mock interview.");
            return;
        }

        try
        {
            var response = await _sender.Send(new CompleteMockInterviewCommand { InterviewId = id }, cancellationToken);
            if (response.Success && response.Data is { } review)
            {
                // Re-read the row for status/timestamps (IUserOwned filter scopes to caller).
                var interview = await _db.MockInterviews
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

                await Clients.Caller.SendAsync("InterviewComplete", new
                {
                    interviewId = id,
                    score = review.Score,
                    status = interview?.Status.ToString(),
                    startedAt = interview?.StartedAt,
                    completedAt = interview?.CompletedAt,
                    review
                }, cancellationToken);
            }
            else
            {
                await SendError(id, response.Message);
            }
        }
        catch (OperationCanceledException)
        {
            // Caller disconnected or host is shutting down — nothing to notify.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InterviewHub CompleteMock failed");
            await SendError(id, DescribeError(ex));
        }
    }

    /// <summary>Sends an InterviewError event to the caller, ignoring a disconnected client.</summary>
    private async Task SendError(Guid? interviewId, string message)
    {
        try
        {
            await Clients.Caller.SendAsync("InterviewError", new { interviewId, message });
        }
        catch
        {
            // Caller disconnected — nothing to notify.
        }
    }

    /// <summary>Maps known exceptions to safe messages; anything else gets a generic one.</summary>
    private static string DescribeError(Exception ex) => ex switch
    {
        MockInterviewGenerationException => "The AI interviewer is temporarily unavailable. Please try again.",
        NotFoundException => ex.Message,
        ConflictException => ex.Message,
        RateLimitException rate => $"Rate limit exceeded. Retry in {rate.RetryAfterSeconds}s.",
        UnauthorizedAccessException => ex.Message,
        ValidationException validation => string.Join(" ", validation.Errors.SelectMany(e => e.Value)),
        _ => "An error occurred while generating the response."
    };
}
