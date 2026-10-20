using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Infrastructure.Jobs;

/// <summary>
/// Durable backstop for abandoned textbook ingestions (#104 CodeRabbit round 6).
/// Every claim schedules its own expiry recovery, but if that Schedule call
/// fails (Hangfire storage hiccup) the chain breaks — and dedup means no future
/// upload re-enqueues the row, so it would sit in Processing forever. The sweep
/// re-enqueues stale claims from their staged file; IngestionJob's RunId fencing
/// makes any overlapping run harmless.
/// </summary>
public sealed class TextbookJobSweeper
{
    private readonly ApplicationDbContext _db;
    private readonly ITextbookIngestionQueue _queue;
    private readonly ILogger<TextbookJobSweeper> _logger;

    public TextbookJobSweeper(
        ApplicationDbContext db,
        ITextbookIngestionQueue queue,
        ILogger<TextbookJobSweeper> logger)
    {
        _db = db;
        _queue = queue;
        _logger = logger;
    }

    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        var staleBefore = DateTime.UtcNow - IngestionJob.StaleClaimThreshold;
        var staleIds = await _db.TextbookJobs.AsNoTracking()
            .Where(job => job.Status == TextbookJobStatus.Processing
                && (job.ClaimedAt == null || job.ClaimedAt < staleBefore))
            .Select(job => job.Id)
            .ToListAsync(cancellationToken);

        var staging = Path.Combine(Path.GetTempPath(), ITextbookIngestionQueue.TempDirectoryName);
        var requeued = 0;
        foreach (var jobId in staleIds)
        {
            try
            {
                // Staged files are named {jobId}{extension} (UploadTextbookHandler);
                // GUID ids prevent the prefix match from hitting another job.
                var stagedFile = Directory.Exists(staging)
                    ? Directory.EnumerateFiles(staging, $"{jobId}*").FirstOrDefault()
                    : null;
                if (stagedFile is null)
                {
                    _logger.LogWarning(
                        "Stale textbook job {JobId} has no staged file; leaving it for manual triage.",
                        jobId);
                    continue;
                }

                _queue.Enqueue(jobId, stagedFile);
                requeued++;
            }
            catch (Exception ex)
            {
                // One bad row must not stall the sweep for the rest.
                _logger.LogError(ex, "Recovery sweep failed to re-enqueue textbook job {JobId}.", jobId);
            }
        }

        if (requeued > 0)
        {
            _logger.LogInformation(
                "Recovery sweep re-enqueued {Count} stale textbook job(s).", requeued);
        }
    }
}
