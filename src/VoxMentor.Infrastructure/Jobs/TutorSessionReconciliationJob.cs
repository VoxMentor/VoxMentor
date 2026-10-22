using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;

namespace VoxMentor.Infrastructure.Jobs;

/// <summary>
/// Every minute (#72): re-queues Pending sessions whose enqueue was lost
/// (crash between save and enqueue) and fails Streaming sessions stuck past
/// the 300s stream cap (crash mid-generation — TutorHub's "reconciliation
/// will sweep" hook). All writes are status-conditional (Status is a
/// concurrency token), so the sweep can never overwrite a result the session
/// job wrote first, and vice versa. Cross-user by design, so the global user
/// filter is bypassed explicitly (a background job has no HTTP user).
/// </summary>
[AutomaticRetry(Attempts = 1)]
[DisableConcurrentExecution(3600)]
public class TutorSessionReconciliationJob
{
    // ponytail: fixed ages; streaming age uses StartedAt (stream start), with
    // CreatedAt only as the fallback for rows created before #72 shipped.
    private static readonly TimeSpan LostPendingAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StuckStreamingAge = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _db;
    private readonly ITutorSessionQueue _queue;
    private readonly ILogger<TutorSessionReconciliationJob> _logger;

    /// <summary>Creates the sweep over the scoped DbContext and session queue.</summary>
    public TutorSessionReconciliationJob(
        ApplicationDbContext db,
        ITutorSessionQueue queue,
        ILogger<TutorSessionReconciliationJob> logger)
    {
        _db = db;
        _queue = queue;
        _logger = logger;
    }

    /// <summary>
    /// Fails stale Streaming rows (age from stream start), then claims stale
    /// Pending rows via QueuedAt before re-enqueueing them so each session is
    /// enqueued at most once per window. Every row is saved individually: a
    /// conflict means another writer finalized it first, which is logged and
    /// skipped rather than failing the whole sweep.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var stuckStreaming = await _db.TutorSessions
            .IgnoreQueryFilters()
            .Where(s => s.Status == TutorSessionStatus.Streaming
                && (s.StartedAt ?? s.CreatedAt) <= now - StuckStreamingAge)
            .ToListAsync(cancellationToken);

        var failed = 0;
        foreach (var session in stuckStreaming)
        {
            session.Status = TutorSessionStatus.Failed;
            session.Error = "Generation timed out.";
            session.CompletedAt = now;
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                failed++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // The session job wrote a terminal state between load and save.
                _db.Entry(session).State = EntityState.Detached;
            }
        }

        // Re-queue only genuinely-lost enqueues: QueuedAt null (crash between
        // save and enqueue) or stale (enqueued but never picked up). Claiming
        // QueuedAt first means the next sweep won't enqueue the same row again.
        var lostPending = await _db.TutorSessions
            .IgnoreQueryFilters()
            .Where(s => s.Status == TutorSessionStatus.Pending
                && s.CreatedAt <= now - LostPendingAge
                && (s.QueuedAt == null || s.QueuedAt <= now - LostPendingAge))
            .ToListAsync(cancellationToken);

        var requeued = 0;
        foreach (var session in lostPending)
        {
            session.QueuedAt = now;
            try
            {
                // Claim before enqueue: a row left Pending by an interrupted
                // enqueue must not flip back to Pending-without-job if this
                // crashes (QueuedAt staleness re-claims it on a later sweep).
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The session job claimed it (Pending → Streaming) — it runs.
                _db.Entry(session).State = EntityState.Detached;
                continue;
            }

            try
            {
                _queue.Enqueue(session.Id);
                requeued++;
            }
            catch (Exception ex)
            {
                // Claim stands; the QueuedAt staleness rule re-claims later.
                _logger.LogWarning(ex, "Could not re-queue pending tutor session {SessionId}.", session.Id);
            }
        }

        if (failed > 0 || requeued > 0)
        {
            _logger.LogInformation(
                "Tutor reconciliation: {Failed} stale Streaming sessions failed, {Requeued} Pending sessions re-queued.",
                failed, requeued);
        }
    }
}
