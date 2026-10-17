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
/// will sweep" hook). Cross-user by design, so the global user filter is
/// bypassed explicitly (a background job has no HTTP user).
/// </summary>
[AutomaticRetry(Attempts = 1)]
[DisableConcurrentExecution(3600)]
public class TutorSessionReconciliationJob
{
    // ponytail: fixed ages from CreatedAt (the only start timestamp on the row);
    // add a StartedAt column if the ages ever need to be per-session.
    private static readonly TimeSpan LostPendingAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StuckStreamingAge = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _db;
    private readonly ITutorSessionQueue _queue;
    private readonly ILogger<TutorSessionReconciliationJob> _logger;

    public TutorSessionReconciliationJob(
        ApplicationDbContext db,
        ITutorSessionQueue queue,
        ILogger<TutorSessionReconciliationJob> logger)
    {
        _db = db;
        _queue = queue;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var stuckStreaming = await _db.TutorSessions
            .IgnoreQueryFilters()
            .Where(s => s.Status == TutorSessionStatus.Streaming
                && s.CreatedAt <= now - StuckStreamingAge)
            .ToListAsync(cancellationToken);
        foreach (var session in stuckStreaming)
        {
            session.Status = TutorSessionStatus.Failed;
            session.Error = "Generation timed out.";
            session.CompletedAt = now;
        }

        var lostPending = await _db.TutorSessions
            .IgnoreQueryFilters()
            .Where(s => s.Status == TutorSessionStatus.Pending
                && s.CreatedAt <= now - LostPendingAge)
            .ToListAsync(cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        // Re-enqueue after the save: a row left Pending by an interrupted
        // enqueue must not flip back to Pending-without-job if this crashes.
        var requeued = 0;
        foreach (var session in lostPending)
        {
            try
            {
                _queue.Enqueue(session.Id);
                requeued++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not re-queue pending tutor session {SessionId}.", session.Id);
            }
        }

        if (stuckStreaming.Count > 0 || requeued > 0)
        {
            _logger.LogInformation(
                "Tutor reconciliation: {Failed} stale Streaming sessions failed, {Requeued} Pending sessions re-queued.",
                stuckStreaming.Count, requeued);
        }
    }
}
