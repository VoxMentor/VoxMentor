using System.Text;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Hangfire job (#72): claims a Pending TutorSession, runs the RAG pipeline
/// via <see cref="ITutorService"/>, and persists answer + token count
/// (Completed) or a stored error message (Failed). The session row is the
/// contract for GET /api/v1/tutor/sessions/{id}. Claims and terminal writes
/// ride on TutorSession.Status being a concurrency token, so duplicate jobs
/// and the reconciliation sweep cannot overwrite each other's results.
/// </summary>
// Failures land on the session row; retrying would only delay the Failed
// status the client is polling for (mirrors IngestionJob).
[AutomaticRetry(Attempts = 0)]
public class TutorSessionJob
{
    private const int MaxErrorMessageLength = 1000;

    private readonly ApplicationDbContext _db;
    private readonly ITutorService _tutor;
    private readonly ILogger<TutorSessionJob> _logger;

    /// <summary>Creates the job over the scoped DbContext and tutor pipeline.</summary>
    public TutorSessionJob(
        ApplicationDbContext db,
        ITutorService tutor,
        ILogger<TutorSessionJob> logger)
    {
        _db = db;
        _tutor = tutor;
        _logger = logger;
    }

    /// <summary>
    /// Processes one session: atomic Pending → Streaming claim, stream the
    /// answer, then a status-conditional Completed/Failed write. Duplicate or
    /// lost jobs exit without side effects.
    /// </summary>
    public async Task Execute(Guid sessionId, CancellationToken cancellationToken)
    {
        TutorSession? session = null;
        try
        {
            // Background job has no HTTP user — bypass the global user filter.
            session = await _db.TutorSessions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
            if (session is null)
            {
                _logger.LogWarning("Tutor session {SessionId} not found; nothing to process.", sessionId);
                return;
            }

            if (session.Status != TutorSessionStatus.Pending)
            {
                // Duplicate enqueue or reconciler already handled it.
                _logger.LogInformation(
                    "Tutor session {SessionId} is {Status}; skipping duplicate job.", sessionId, session.Status);
                return;
            }

            // Claim: Status is a concurrency token, so the Pending → Streaming
            // write is atomic per session — the losing duplicate gets a
            // DbUpdateConcurrencyException and exits. Sessions therefore run
            // concurrently instead of serializing on a global job lock.
            session.Status = TutorSessionStatus.Streaming;
            session.StartedAt = DateTime.UtcNow;
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogInformation(
                    "Tutor session {SessionId} was claimed by another worker; skipping duplicate job.", sessionId);
                _db.Entry(session).State = EntityState.Detached;
                return;
            }

            var answer = new StringBuilder();
            var totalTokens = 0;
            var sawFinal = false;
            await foreach (var chunk in _tutor.StreamAnswerAsync(
                session.Question, session.ConceptId, cancellationToken))
            {
                answer.Append(chunk.Text);
                if (chunk.IsFinal)
                {
                    sawFinal = true;
                    totalTokens = chunk.EvalCount ?? totalTokens;
                }
            }

            if (!sawFinal)
            {
                // EOF before done=true: never persist a partial answer as Completed.
                await MarkFailedAsync(session, "Generation ended without a final stream chunk.");
                return;
            }

            session.Answer = answer.ToString();
            session.TotalTokens = totalTokens;
            session.Status = TutorSessionStatus.Completed;
            session.CompletedAt = DateTime.UtcNow;
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Reconciliation already failed this session (queue delay ate
                // the stream-start budget) — its terminal result stands.
                _logger.LogInformation(
                    "Tutor session {SessionId} was finalized elsewhere; not overwriting with Completed.", sessionId);
                _db.Entry(session).State = EntityState.Detached;
                return;
            }

            _logger.LogInformation(
                "Tutor session {SessionId} completed ({Tokens} tokens).", sessionId, totalTokens);
        }
        catch (OperationCanceledException)
        {
            await MarkFailedAsync(session, "Generation was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tutor generation failed for session {SessionId}.", sessionId);
            // ponytail: no rethrow — failure lives on the row; Hangfire retry is off.
            await MarkFailedAsync(session, ex.Message);
        }
    }

    /// <summary>
    /// Writes Failed + truncated error message in one status-conditional save.
    /// A concurrency conflict means another writer already finalized the row
    /// and is logged, never overwritten.
    /// </summary>
    private async Task MarkFailedAsync(TutorSession? session, string message)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            session.Answer ??= string.Empty;
            session.Status = TutorSessionStatus.Failed;
            session.Error = message.Length > MaxErrorMessageLength
                ? message[..MaxErrorMessageLength]
                : message;
            session.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogInformation(
                "Tutor session {SessionId} was finalized elsewhere; not overwriting with Failed.", session.Id);
            _db.Entry(session).State = EntityState.Detached;
        }
        catch (Exception ex)
        {
            // Never let error handling fault the Hangfire worker; the
            // reconciliation sweep is the backstop for a stuck Streaming row.
            _logger.LogError(ex, "Could not mark tutor session {SessionId} as failed.", session.Id);
        }
    }
}
