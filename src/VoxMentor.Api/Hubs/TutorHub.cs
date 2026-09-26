using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Api.Hubs;

/// <summary>
/// AI tutor streaming hub (#73). AskTutor runs the RAG pipeline via
/// <see cref="ITutorService"/> and forwards tokens as they are generated:
/// TutorToken* → TutorComplete, or TutorError on failure. The session row is
/// persisted so GET /api/v1/tutor/sessions/{id} sees the same state.
/// </summary>
[Authorize(Roles = "Student")]
public class TutorHub : Hub
{
    private readonly ITutorService _tutor;
    private readonly IApplicationDbContext _db;
    private readonly IRateLimiter _rateLimiter;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<TutorHub> _logger;

    private const int MaxQuestionLength = 2000;

    // ponytail: per-user in-flight gate, one entry per user asking right now.
    // Static = hub instances are per-invocation; dictionary holds nothing when idle.
    private static readonly ConcurrentDictionary<string, byte> InFlightAsks = new();

    public TutorHub(
        ITutorService tutor,
        IApplicationDbContext db,
        IRateLimiter rateLimiter,
        ICurrentUserService currentUser,
        ILogger<TutorHub> logger)
    {
        _tutor = tutor;
        _db = db;
        _rateLimiter = rateLimiter;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task AskTutor(string conceptId, string question)
    {
        Guid? parsedConceptId = null;
        if (!string.IsNullOrWhiteSpace(conceptId))
        {
            if (!Guid.TryParse(conceptId, out var cid))
            {
                await SendError(null, $"Invalid conceptId '{conceptId}'.");
                return;
            }
            parsedConceptId = cid;
        }

        question = question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            await SendError(null, "Question must not be empty.");
            return;
        }
        if (question.Length > MaxQuestionLength)
        {
            await SendError(null, $"Question must be at most {MaxQuestionLength} characters.");
            return;
        }

        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            await SendError(null, "User must be authenticated to ask the tutor.");
            return;
        }

        // Same window as POST /api/v1/tutor/ask — the hub must not bypass it.
        try
        {
            await _rateLimiter.CheckAsync($"tutor:ask:{userId}", Context.ConnectionAborted);
        }
        catch (RateLimitException ex)
        {
            await SendError(null, $"Rate limit exceeded. Retry in {ex.RetryAfterSeconds}s.");
            return;
        }

        if (parsedConceptId.HasValue &&
            !await _db.Concepts.AnyAsync(c => c.Id == parsedConceptId, Context.ConnectionAborted))
        {
            await SendError(null, $"Concept {parsedConceptId} was not found.");
            return;
        }

        // One ask per user at a time — bounds 300s streams per account (#73 security review).
        if (!InFlightAsks.TryAdd(userId, 0))
        {
            await SendError(null, "You already have a tutor answer in progress. Wait for it to finish.");
            return;
        }

        try
        {
            var session = new TutorSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ConceptId = parsedConceptId,
                Question = question,
                Status = TutorSessionStatus.Streaming
            };
            _db.TutorSessions.Add(session);
            await _db.SaveChangesAsync(Context.ConnectionAborted);

            var answer = new StringBuilder();
            var totalTokens = 0;
            var sawFinalChunk = false;
            try
            {
                await foreach (var chunk in _tutor.StreamAnswerAsync(question, parsedConceptId, Context.ConnectionAborted))
                {
                    if (chunk.Text.Length > 0)
                    {
                        answer.Append(chunk.Text);
                        await Clients.Caller.SendAsync("TutorToken", chunk.Text, Context.ConnectionAborted);
                    }
                    if (chunk.IsFinal)
                    {
                        totalTokens = chunk.EvalCount ?? totalTokens;
                        sawFinalChunk = true;
                    }
                }

                if (!sawFinalChunk)
                {
                    _logger.LogWarning("Tutor stream ended without a final chunk (no done=true); session {SessionId} will complete with totalTokens={TotalTokens}",
                        session.Id, totalTokens);
                }

                session.Answer = answer.ToString();
                session.TotalTokens = totalTokens;
                session.Status = TutorSessionStatus.Completed;
                session.CompletedAt = DateTime.UtcNow;
                try
                {
                    await _db.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    // ponytail: DB persistence failed but answer was generated — reconciliation (#72) will sweep.
                    // Still notify the client so the UI doesn't hang.
                    _logger.LogError(ex, "Failed to persist completed TutorSession {SessionId}", session.Id);
                }

                try
                {
                    await Clients.Caller.SendAsync("TutorComplete", new
                    {
                        sessionId = session.Id,
                        totalTokens
                    });
                }
                catch (Exception ex)
                {
                    // ponytail: delivery failed after generation succeeded — session stays
                    // Completed; a dead client doesn't get to flip it to Failed (#89 F1).
                    _logger.LogWarning(ex, "Failed to send TutorComplete for session {SessionId}", session.Id);
                }
            }
            catch (OperationCanceledException)
            {
                session.Answer = answer.ToString();
                await MarkFailed(session, "Generation was cancelled.");
            }
            catch (Exception ex)
            {
                session.Answer = answer.ToString();
                await MarkFailed(session, SanitizeError(ex));
            }
        }
        finally
        {
            InFlightAsks.TryRemove(userId, out _);
        }
    }

    private async Task MarkFailed(TutorSession session, string message)
    {
        session.Status = TutorSessionStatus.Failed;
        session.CompletedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // ponytail: log the failure but don't throw — session state may be lost.
            // A background reconciliation job should sweep stale Streaming sessions.
            _logger.LogError(ex, "Failed to persist TutorSession {SessionId} as Failed", session.Id);
        }
        await SendError(session.Id, message);
    }

private async Task SendError(Guid? sessionId, string message)
        {
            try
            {
                await Clients.Caller.SendAsync("TutorError", new { sessionId, message });
            }
            catch
            {
                // Caller disconnected — nothing to notify.
            }
        }

        private static string SanitizeError(Exception ex)
        {
            // ponytail: never leak internal details to the client.
            // Log the full exception server-side; return a generic message.
            return "An error occurred while generating the response.";
        }
}
