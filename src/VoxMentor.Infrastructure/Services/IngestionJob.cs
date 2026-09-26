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

        try
        {
            var job = await _db.TextbookJobs
                .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
            if (job is null)
            {
                _logger.LogWarning("Textbook job {JobId} not found; nothing to process.", jobId);
                return;
            }

            job.Status = TextbookJobStatus.Processing;
            job.ProcessedChunks = 0;
            // Crash-requeue idempotency: drop chunks from any previous partial run.
            var staleChunks = await _db.TextbookChunks
                .Where(c => c.JobId == jobId)
                .ToListAsync(cancellationToken);
            _db.TextbookChunks.RemoveRange(staleChunks);
            await _db.SaveChangesAsync(cancellationToken);

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

                _db.TextbookChunks.Add(new TextbookChunk
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    ConceptId = job.ConceptId,
                    Content = chunks[i],
                    Source = job.FileName,
                    Embedding = new Vector(embedding)
                });
                job.ProcessedChunks = i + 1;
                await _db.SaveChangesAsync(cancellationToken);
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
                await MarkFailedAsync(jobId, ex, CancellationToken.None);
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

    private async Task MarkFailedAsync(Guid jobId, Exception error, CancellationToken cancellationToken)
    {
        var job = await _db.TextbookJobs
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = TextbookJobStatus.Failed;
        job.Error = error.Message.Length > MaxErrorMessageLength
            ? error.Message[..MaxErrorMessageLength]
            : error.Message;
        job.CompletedAt = DateTime.UtcNow;

        // Drop pending in-memory inserts first — re-saving them would rethrow
        // the original error and escape, faulting the Hangfire worker.
        foreach (var entry in _db.ChangeTracker.Entries<TextbookChunk>()
                     .Where(e => e.State == EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }

        // Drop the partial corpus so retrieval never sees a half-embedded file.
        var partialChunks = await _db.TextbookChunks
            .Where(c => c.JobId == jobId)
            .ToListAsync(cancellationToken);
        _db.TextbookChunks.RemoveRange(partialChunks);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
