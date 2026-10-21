using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;
using VoxMentor.Infrastructure.Plagiarism;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Hangfire job: extract text → chunk → embed via nomic-embed-text → store
/// TextbookChunks with pgvector vectors. Progress and failures are recorded
/// on the TextbookJob row for the status endpoint.
/// </summary>
// ponytail: failures are recorded on the job row, not retried — an admin
// re-uploads on transient Ollama errors; enable AutomaticRetry if that gets noisy.
[AutomaticRetry(Attempts = 0)]
// Narrows concurrent execution via the storage lock (seconds); the stale-chunk
// purge below is the real guarantee (Hangfire: the lock alone is not reliable).
// ponytail: 1 h lock timeout — only guards zombie connections; a dead worker's
// dropped connection releases the lock immediately.
[DisableConcurrentExecution(3600)]
public class IngestionJob
{
    private const int MaxErrorMessageLength = 1000;

    // ponytail: a claim older than the Hangfire lock timeout is treated as
    // abandoned (worker crashed or its connection dropped) and may be
    // reclaimed by the next run, so a dead claimant cannot strand a job
    // as Processing forever (#104 CodeRabbit).
    // Shared with TextbookJobSweeper (#104 round 6): the sweep only re-enqueues
    // rows whose claim is at least this old, matching the staleness the
    // reclaim path applies here.
    internal static readonly TimeSpan StaleClaimThreshold = TimeSpan.FromHours(1);

    // ponytail: lexical confinement only — reconstruct path from jobId if job
    // arguments ever become untrusted cross-tenant.
    private static readonly string TempDirectory =
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), ITextbookIngestionQueue.TempDirectoryName))
        + Path.DirectorySeparatorChar;

    private readonly ApplicationDbContext _db;
    private readonly CodeEmbeddingService _embedder;
    private readonly ITextbookIngestionQueue _schedule;
    private readonly ILogger<IngestionJob> _logger;

    public IngestionJob(
        ApplicationDbContext db,
        CodeEmbeddingService embedder,
        ITextbookIngestionQueue schedule,
        ILogger<IngestionJob> logger)
    {
        _db = db;
        _embedder = embedder;
        _schedule = schedule;
        _logger = logger;
    }

    public async Task Execute(Guid jobId, string filePath, CancellationToken cancellationToken)
    {
        // Guard precedes try/finally so an untrusted path is never deleted or read.
        if (!IsAllowedTempFile(filePath))
        {
            _logger.LogWarning(
                "Textbook job {JobId} referenced a file outside {Dir}; ignoring.", jobId, TempDirectory);
            return;
        }

        var savedChunkIds = new List<Guid>();
        var runId = Guid.NewGuid();
        // ponytail: only the run that claims the row may delete the staged
        // file — replayed executions of one Hangfire job share the same
        // filePath, and losing runs must leave the input for the owner and
        // any scheduled recovery run (#104 CodeRabbit round 5).
        var ownsStagedFile = true;
        try
        {
            var job = await _db.TextbookJobs
                .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
            if (job is null)
            {
                _logger.LogWarning("Textbook job {JobId} not found; nothing to process.", jobId);
                return;
            }

            // Failed is sticky (#104 CodeRabbit): the legacy migration and the
            // upload handler's cleanup end rows as Failed, but Hangfire may
            // still hold a queued job for them. Re-running would resurrect
            // failed work; the finally block cleans the staged file.
            // Completed is sticky for the same reason: a re-queued Completed
            // run deletes the winner's published corpus at the stale-chunk
            // purge and dies on the already-deleted temp file, and an empty
            // Completed row is never repairable (dedup blocks the re-upload).
            if (job.Status is TextbookJobStatus.Failed or TextbookJobStatus.Completed)
            {
                _logger.LogWarning("Textbook job {JobId} is already {Status}; skipping.", jobId, job.Status);
                return;
            }

            // RunId ownership (#104 CodeRabbit): Hangfire's
            // DisableConcurrentExecution is a best-effort storage lock, so a
            // connection loss can let two Execute calls overlap on the same
            // row. Only the run that claimed the job may publish or fail it;
            // every other run backs off — unless the claim is stale
            // (ClaimedAt older than the lock timeout), in which case the
            // claiming worker is dead and this run takes over.
            if (job.RunId is not null && job.RunId != runId)
            {
                var stale = job.ClaimedAt is null
                    || job.ClaimedAt < DateTime.UtcNow - StaleClaimThreshold;
                if (stale)
                {
                    _logger.LogWarning(
                        "Textbook job {JobId} claim by {RunId} is stale; reclaiming.", jobId, job.RunId);
                }
                else
                {
                    _logger.LogWarning(
                        "Textbook job {JobId} is owned by another run; backing off.", jobId);
                    // The claim is fresh — either a live owner is processing
                    // it, or the owner crashed minutes ago and Hangfire's
                    // invisibility-timeout requeue sent this run before the
                    // claim went stale. With Attempts=0 this invocation is
                    // the last one, so schedule a recovery run for the claim
                    // expiry; the staged file survives this run for it.
                    // Non-owner first: a throwing Schedule must not fall
                    // through to the file-deleting finally.
                    // ponytail: remaining is ≥ 0 to within the µs between
                    // the two UtcNow reads; a zero/negative delay just
                    // re-runs immediately and reclaims once the claim
                    // crosses the threshold.
                    ownsStagedFile = false;
                    var remaining =
                        StaleClaimThreshold - (DateTime.UtcNow - job.ClaimedAt!.Value);
                    TryScheduleRecovery(jobId, filePath, remaining);
                    return;
                }
            }

            job.Status = TextbookJobStatus.Processing;
            job.RunId = runId;
            job.ClaimedAt = DateTime.UtcNow;
            job.ProcessedChunks = 0;
            // Crash-requeue idempotency: drop chunks from any previous partial run.
            var staleChunks = await _db.TextbookChunks
                .Where(c => c.JobId == jobId)
                .ToListAsync(cancellationToken);
            _db.TextbookChunks.RemoveRange(staleChunks);
            try
            {
                // The RunId concurrency token makes this claim atomic: a
                // concurrent claim on the same Pending row fails here and the
                // loser backs off above on its next load.
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogWarning(
                    "Textbook job {JobId} was claimed by another run; backing off.", jobId);
                // Just lost the claim race to a run that committed a moment
                // ago — usually a live owner that finishes the job, but it
                // can die before scheduling its own watchdog, so hand one in
                // as well (a no-op once the row is terminal).
                ownsStagedFile = false;
                TryScheduleRecovery(jobId, filePath, StaleClaimThreshold);
                return;
            }

            // Every claim carries its own expiry watchdog: a hung worker
            // (alive past the threshold, but silent) keeps Hangfire's
            // heartbeat, so no invisibility requeue ever replays the job and
            // no losing run ever exists to schedule recovery. The watchdog
            // fires at the claim expiry — terminal rows no-op on it, a live
            // run heartbeats past it, a dead one is reclaimed.
            TryScheduleRecovery(jobId, filePath, StaleClaimThreshold);

            var text = await TextExtractor.ExtractAsync(filePath, cancellationToken);
            var chunks = TextChunker.Chunk(text);
            if (chunks.Count == 0)
            {
                throw new InvalidOperationException(
                    "No extractable text found in the file (scanned or image-only PDF?).");
            }

            job.TotalChunks = chunks.Count;
            await _db.SaveChangesAsync(cancellationToken);

            for (var i = 0; i < chunks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var embedding = await _embedder.EmbedAsync(chunks[i], cancellationToken);
                if (embedding is null)
                {
                    throw new InvalidOperationException(
                        $"Embedding failed for chunk {i + 1}/{chunks.Count} (Ollama unavailable or nomic-embed-text not pulled?).");
                }

                var chunkId = Guid.NewGuid();
                _db.TextbookChunks.Add(new TextbookChunk
                {
                    Id = chunkId,
                    JobId = job.Id,
                    ConceptId = job.ConceptId,
                    Content = chunks[i],
                    Source = job.FileName,
                    Embedding = new Vector(embedding)
                });
                savedChunkIds.Add(chunkId);
                job.ProcessedChunks = i + 1;
                // Heartbeat the claim per chunk: recoveries treat the row as
                // abandoned only after a full threshold with no save, so a
                // live run is not reclaimed mid-work unless one
                // extract/embed gap outlives the threshold
                // (#104 CodeRabbit round 5).
                job.ClaimedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }

            // A concurrent migration or handler may have ended this job while we
            // were mid-run (legacy transition, duplicate reconciliation). Never
            // publish Completed over a terminal row — that would mask a truncated
            // corpus behind dedup's "already ingested" answer.
            var current = await _db.TextbookJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
            if (current is null || current.Status != TextbookJobStatus.Processing)
            {
                _logger.LogWarning(
                    "Textbook job {JobId} is no longer Processing ({Status}); aborting without publishing Completed.",
                    jobId, current?.Status);
                // A worker between per-chunk saves can have re-saved chunks
                // after the reconciliation deleted the earlier ones
                // (TextbookChunks has no FK back to the job). Drop the
                // residue so a Failed row never carries a partial corpus
                // into retrieval (#104 CodeRabbit). Only Failed is purged
                // wholesale: a Completed row means a winner published, and
                // only this run's persisted chunks (tracked by Id) are
                // dropped — the winner's published corpus stays intact.
                if (current?.Status == TextbookJobStatus.Failed)
                {
                    await PurgeJobChunksAsync(jobId);
                }
                else if (current?.Status == TextbookJobStatus.Completed &&
                         savedChunkIds.Count > 0)
                {
                    await PurgeJobChunksAsync(jobId, savedChunkIds);
                }
                return;
            }

            // Publish only while still the claimant (#104 CodeRabbit): a
            // superseded run drops only its own chunks and returns without
            // touching the winner's row or corpus. Compare against the store
            // read above — the tracked instance always matches this run's
            // own writes, so it can never detect a mid-run claim rotation.
            if (current.RunId is not null && current.RunId != runId)
            {
                // A live owner holds the row now (fresh claim rotation); it
                // may still be mid-extraction, so this run must not delete
                // the staged file.
                ownsStagedFile = false;
                if (savedChunkIds.Count > 0)
                {
                    await PurgeJobChunksAsync(jobId, savedChunkIds);
                }
                return;
            }

            job.Status = TextbookJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            // Rotate the token on the terminal write (#104 v3 CS-5): a
            // recovery that loaded the row before this save must not reclaim
            // Completed back to Processing, and a zombie that loaded earlier
            // must not publish over the winner.
            job.RunId = null;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Textbook job {JobId} completed: {Count} chunks from {FileName}.",
                jobId, chunks.Count, job.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Textbook ingestion failed for job {JobId}.", jobId);
            // ponytail: no rethrow — failure lives on the row; Hangfire retry is off anyway.
            try
            {
                if (await MarkFailedAsync(jobId, ex, CancellationToken.None, savedChunkIds, runId))
                {
                    // The failure probe found a foreign owner that may still
                    // be processing; the staged file is kept for it.
                    ownsStagedFile = false;
                }
            }
            catch (Exception markEx)
            {
                // The failure write is unconfirmed — the row may still be
                // Processing with a claim someone else can reclaim. Keep the
                // staged file: a leaked temp file is better than deleting the
                // input a future run needs (#104 review gate: CR5 R1,
                // SEC6 O1). Never let error handling fault the Hangfire
                // worker either.
                ownsStagedFile = false;
                _logger.LogError(markEx, "Could not mark job {JobId} as failed.", jobId);
            }
        }
        finally
        {
            if (ownsStagedFile)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not delete temp file {FilePath}.", filePath);
                }
            }
        }
    }

    private static bool IsAllowedTempFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception)
        {
            return false; // ponytail: malformed path from a tampered job argument
        }

        if (!fullPath.StartsWith(TempDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(fullPath);
        return extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Best-effort recovery scheduling: the recovery run is a backstop, so a
    /// queue/storage hiccup must never fail (or mask) the current run.
    /// </summary>
    private void TryScheduleRecovery(Guid jobId, string filePath, TimeSpan delay)
    {
        try
        {
            _schedule.Schedule(jobId, filePath, delay);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Could not schedule a recovery run for textbook job {JobId}.", jobId);
        }
    }

    /// <summary>
    /// Records the failure under the row's ownership rules. Returns true when
    /// the probe found a foreign owner that may still be processing the job —
    /// the caller must then keep the staged file for it (#104 CodeRabbit
    /// round 5); false when this run ended the row or there is nothing left.
    /// </summary>
    private async Task<bool> MarkFailedAsync(Guid jobId, Exception error,
                                       CancellationToken cancellationToken,
                                       IReadOnlyList<Guid> savedChunkIds,
                                       Guid runId)
    {
        var job = await _db.TextbookJobs
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return false;
        }

        // Identity resolution can hand back this worker's own tracked
        // instance, whose Status is stale against the store: a reconciler
        // on a separate connection (or a zombie winner) may have ended the
        // row while this run was in-flight, and EF does not refresh tracked
        // instances from the database. Probe the store to decide what this
        // failure may write.
        var stored = await _db.TextbookJobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new { j.Status, j.RunId })
            .FirstOrDefaultAsync(cancellationToken);

        // A winner published out from under this run: never Fail its row or
        // purge its corpus behind dedup's "already ingested" reuse.
        if (stored?.Status == TextbookJobStatus.Completed)
        {
            // ponytail: this run's tracked row still carries the RunId-
            // rejected update; detach it so the purge's SaveChanges flushes
            // only the chunk deletes instead of re-throwing and rolling
            // them back (#104 CodeRabbit round 5).
            _db.Entry(job).State = EntityState.Detached;
            if (savedChunkIds.Count > 0)
            {
                // Winner published first: clean only this run's persisted
                // chunks and keep the winner's corpus intact.
                await PurgeJobChunksAsync(jobId, savedChunkIds);
            }
            _logger.LogWarning(
                "Textbook job {JobId} is already Completed; not marking Failed and keeping its corpus.",
                jobId);
            return false;
        }

        // A different run owns this job (or ended it with another RunId):
        // never mark Failed or purge wholesale — drop only this run's
        // persisted chunks and return. The row may still be Processing under
        // that run, which can be mid-extraction: keep the staged file for it.
        if (stored?.RunId is not null && stored?.RunId != runId)
        {
            // Same detached-update problem as the Completed branch above:
            // the rejected save must not be re-flushed by the cleanup.
            _db.Entry(job).State = EntityState.Detached;
            if (savedChunkIds.Count > 0)
            {
                await PurgeJobChunksAsync(jobId, savedChunkIds);
            }
            _logger.LogWarning(
                "Textbook job {JobId} is owned by another run; not marking Failed.",
                jobId);
            // Keep the staged file only while the foreign run can still work:
            // a row it already ended as Failed has no consumer left.
            return stored.Status != TextbookJobStatus.Failed;
        }

        // An already-Failed row carries its ender's explanatory Error
        // (reconciler or another failure route); a re-mark must not
        // overwrite that metadata, nor its CompletedAt.
        var wasFailed = stored?.Status == TextbookJobStatus.Failed;
        job.Status = TextbookJobStatus.Failed;
        // Same terminal-token rotation as the Completed publish: once the row
        // is Failed, an earlier-loaded recovery cannot reclaim it, and a
        // zombie whose save was already rejected stays rejected (#104 v3).
        job.RunId = null;
        if (!wasFailed)
        {
            job.Error = error.Message.Length > MaxErrorMessageLength
                ? error.Message[..MaxErrorMessageLength]
                : error.Message;
            if (job.CompletedAt is null)
            {
                job.CompletedAt = DateTime.UtcNow;
            }
        }

        // Drop pending in-memory inserts + the partial corpus so retrieval
        // never sees a half-embedded file. The helper's SaveChanges also
        // flushes the Failed fields above in the same atomic batch.
        await PurgeJobChunksAsync(jobId, savedChunkIds);
        return false;
    }

    private async Task PurgeJobChunksAsync(Guid jobId)
    {
        // Full purge: used when this run owns every chunk of the job
        // (normal failure path, or a Failed-abort where no winner exists).
        await PurgeJobChunksAsync(jobId, null);
    }

    private async Task PurgeJobChunksAsync(Guid jobId, IReadOnlyList<Guid>? chunkIds)
    {
        // Pending in-memory inserts first — re-saving them would rethrow the
        // original error and escape, faulting the Hangfire worker.
        foreach (var entry in _db.ChangeTracker.Entries<TextbookChunk>()
                     .Where(e => e.State == EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }

        // ponytail: tracked purge (not ExecuteDelete) — the InMemory test
        // contexts can't run relational bulk ops; swap if the suite moves to SQLite.
        var residue = await _db.TextbookChunks
            .Where(c => c.JobId == jobId)
            .ToListAsync(CancellationToken.None);
        _db.TextbookChunks.RemoveRange(chunkIds is null
            ? residue
            : residue.Where(c => chunkIds.Contains(c.Id)));

        await _db.SaveChangesAsync(CancellationToken.None);
    }
}
