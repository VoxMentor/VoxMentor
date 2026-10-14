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
/// Hangfire job (#72): picks up a Pending TutorSession, runs the RAG pipeline
/// via <see cref="ITutorService"/>, and persists answer + token count
/// (Completed) or a stored error message (Failed). The session row is the
/// contract for GET /api/v1/tutor/sessions/{id}.
/// </summary>
// Failures land on the session row; retrying would only delay the Failed
// status the client is polling for (mirrors IngestionJob).
[AutomaticRetry(Attempts = 0)]
// One session at a time: the Pending claim below is a read-then-write with no
// row lock, and DisableConcurrentExecution is what makes it safe.
// ponytail: global lock — throughput ceiling is one stream at a time; move to
// per-session claims (UPDATE ... WHERE Status='Pending') if ever needed.
[DisableConcurrentExecution(3600)]
public class TutorSessionJob
{
    private const int MaxErrorMessageLength = 1000;

    private readonly ApplicationDbContext _db;
    private readonly ITutorService _tutor;
    private readonly ILogger<TutorSessionJob> _logger;

    public TutorSessionJob(
        ApplicationDbContext db,
        ITutorService tutor,
        ILogger<TutorSessionJob> logger)
    {
        _db = db;
        _tutor = tutor;
        _logger = logger;
    }

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

            session.Status = TutorSessionStatus.Streaming;
            await _db.SaveChangesAsync(cancellationToken);

            var answer = new StringBuilder();
            var totalTokens = 0;
            await foreach (var chunk in _tutor.StreamAnswerAsync(
                session.Question, session.ConceptId, cancellationToken))
            {
                answer.Append(chunk.Text);
                if (chunk.IsFinal)
                {
                    totalTokens = chunk.EvalCount ?? totalTokens;
                }
            }

            session.Answer = answer.ToString();
            session.TotalTokens = totalTokens;
            session.Status = TutorSessionStatus.Completed;
            session.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

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
        catch (Exception ex)
        {
            // Never let error handling fault the Hangfire worker; the
            // reconciliation sweep is the backstop for a stuck Streaming row.
            _logger.LogError(ex, "Could not mark tutor session {SessionId} as failed.", session.Id);
        }
    }
}
