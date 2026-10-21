using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Infrastructure.Jobs;

/// <summary>
/// Durable backstop for abandoned textbook ingestions (#104 CodeRabbit rounds 6-7).
/// Every claim schedules its own expiry recovery, but if that Schedule call
/// fails (Hangfire storage hiccup) the chain breaks — and dedup means no future
/// upload re-enqueues the row. Pending rows have the same problem when the
/// process dies between saving the row and Enqueue, or the queue entry is lost.
/// The sweep re-enqueues stale claims and old Pending rows from their staged
/// file; IngestionJob's RunId fencing makes any overlapping run harmless.
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
            .Where(job =>
                (job.Status == TextbookJobStatus.Processing
                    && (job.ClaimedAt == null || job.ClaimedAt < staleBefore))
                // Pending rows have not been claimed yet, so age is the only
                // signal: a crash between save and Enqueue (or a lost Hangfire
                // entry) strands them otherwise — dedup reuses the row forever.
                || (job.Status == TextbookJobStatus.Pending && job.CreatedAt < staleBefore))
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
                    // ponytail: fileless = unrecoverable. The input the worker
                    // needs is gone (deploy wiped /tmp, OS temp cleanup), so no
                    // re-enqueue can ever succeed — and dedup hands this dead
                    // row back on every re-upload, wedging the (hash, concept)
                    // key permanently. Fail it so a re-upload creates a fresh
                    // row. The stale snapshot can span a completion, so only
                    // rows still live at load time are eligible. Tracked save
                    // (not ExecuteUpdate) so the RunId concurrency token
                    // fences a run that completes or reclaims mid-save.
                    var job = await _db.TextbookJobs.FirstOrDefaultAsync(
                        j => j.Id == jobId
                            && (j.Status == TextbookJobStatus.Processing
                                || j.Status == TextbookJobStatus.Pending),
                        cancellationToken);
                    if (job is not null)
                    {
                        // TextbookChunks has no FK back to the job, so a
                        // partially ingested row can still carry chunks; purge
                        // them with the Failed flip so a Failed row never
                        // serves retrieval and a re-upload cannot retrieve the
                        // same content twice (#104 review gate).
                        var orphanChunks = await _db.TextbookChunks
                            .Where(c => c.JobId == jobId)
                            .ToListAsync(cancellationToken);
                        _db.TextbookChunks.RemoveRange(orphanChunks);
                        job.Status = TextbookJobStatus.Failed;
                        // Rotate the token on the terminal write: a run still
                        // holding the old RunId cannot save again, so it can
                        // neither re-insert chunks after this purge nor
                        // overwrite the Failed flip (#104 v3 CS-1/SWEEP-01).
                        job.RunId = null;
                        job.Error = "Staged upload file was lost (host restart or temp cleanup). Re-upload the file to retry.";
                        job.CompletedAt = DateTime.UtcNow;
                        await _db.SaveChangesAsync(cancellationToken);
                        _logger.LogWarning(
                            "Stale textbook job {JobId} has no staged file; marked Failed so a re-upload can start fresh.",
                            jobId);
                    }

                    continue;
                }

                _queue.Enqueue(jobId, stagedFile);
                requeued++;
            }
            catch (Exception ex)
            {
                // One bad row must not stall the sweep for the rest. A failed
                // save leaves its Modified/Deleted entries tracked (EF does
                // not reset them), so the next row's SaveChanges would try to
                // re-flush them; clear before continuing.
                _db.ChangeTracker.Clear();
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
