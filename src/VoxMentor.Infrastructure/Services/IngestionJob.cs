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
    private static readonly TimeSpan StaleClaimThreshold = TimeSpan.FromHours(1);

    // ponytail: lexical confinement only — reconstruct path from jobId if job
    // arguments ever become untrusted cross-tenant.
    private static readonly string TempDirectory =
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), ITextbookIngestionQueue.TempDirectoryName))
        + Path.DirectorySeparatorChar;

    private readonly ApplicationDbContext _db;
    private readonly CodeEmbeddingService _embedder;
    private readonly ILogger<IngestionJob> _logger;

    public IngestionJob(
        ApplicationDbContext db,
        CodeEmbeddingService embedder,
        ILogger<IngestionJob> logger)
    {
        _db = db;
        _embedder = embedder;
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
                return;
            }

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
            // touching the winner's row or corpus.
            if (job.Status != TextbookJobStatus.Processing || job.RunId != runId)
            {
                if (savedChunkIds.Count > 0)
                {
                    await PurgeJobChunksAsync(jobId, savedChunkIds);
                }
                return;
            }

            job.Status = TextbookJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
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
                await MarkFailedAsync(jobId, ex, CancellationToken.None, savedChunkIds, runId);
            }
            catch (Exception markEx)
            {
                // Never let error handling fault the Hangfire worker.
                _logger.LogError(markEx, "Could not mark job {JobId} as failed.", jobId);
            }
        }
        finally
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

    private async Task MarkFailedAsync(Guid jobId, Exception error,
                                       CancellationToken cancellationToken,
                                       IReadOnlyList<Guid> savedChunkIds,
                                       Guid runId)
    {
        var job = await _db.TextbookJobs
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return;
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
            if (savedChunkIds.Count > 0)
            {
                // Winner published first: clean only this run's persisted
                // chunks and keep the winner's corpus intact.
                await PurgeJobChunksAsync(jobId, savedChunkIds);
            }
            _logger.LogWarning(
                "Textbook job {JobId} is already Completed; not marking Failed and keeping its corpus.",
                jobId);
            return;
        }

        // A different run owns this job (or ended it with another RunId):
        // never mark Failed or purge wholesale — drop only this run's
        // persisted chunks and return.
        if (stored?.RunId is not null && stored?.RunId != runId)
        {
            if (savedChunkIds.Count > 0)
            {
                await PurgeJobChunksAsync(jobId, savedChunkIds);
            }
            _logger.LogWarning(
                "Textbook job {JobId} is owned by another run; not marking Failed.",
                jobId);
            return;
        }

        // An already-Failed row carries its ender's explanatory Error
        // (reconciler or another failure route); a re-mark must not
        // overwrite that metadata, nor its CompletedAt.
        var wasFailed = stored?.Status == TextbookJobStatus.Failed;
        job.Status = TextbookJobStatus.Failed;
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
