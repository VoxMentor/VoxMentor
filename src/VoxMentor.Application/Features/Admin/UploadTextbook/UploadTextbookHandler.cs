using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.Admin.UploadTextbook;

/// <summary>
/// Saves the uploaded file to temp storage, records a Pending TextbookJob,
/// and queues background chunking + embedding.
/// </summary>
public class UploadTextbookHandler : IRequestHandler<UploadTextbookCommand, ApiResponse<UploadTextbookResultDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ITextbookIngestionQueue _queue;

    public UploadTextbookHandler(IApplicationDbContext db, ITextbookIngestionQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    public async Task<ApiResponse<UploadTextbookResultDto>> Handle(
        UploadTextbookCommand request, CancellationToken cancellationToken)
    {
        if (request.ConceptId.HasValue)
        {
            var conceptExists = await _db.Concepts
                .AnyAsync(c => c.Id == request.ConceptId.Value, cancellationToken);
            if (!conceptExists)
            {
                throw new NotFoundException($"Concept {request.ConceptId} was not found.");
            }
        }

        var jobId = Guid.NewGuid();
        // ponytail: local temp dir — single-node dev; shared volume/S3 if API ever runs multi-replica.
        var directory = Path.Combine(Path.GetTempPath(), ITextbookIngestionQueue.TempDirectoryName);
        Directory.CreateDirectory(directory);
        var extension = Path.GetExtension(request.FileName);
        var filePath = Path.Combine(directory, $"{jobId}{extension}");

        TextbookJob? job = null;
        var jobSaved = false;
        try
        {
            await using (request.Content)
            {
                await using var fileStream = File.Create(filePath);
                await request.Content.CopyToAsync(fileStream, cancellationToken);
            }

            // Idempotent retries (#104): a lost 202 response makes the admin
            // re-upload the same file; keying on (content, concept) reuses the
            // existing job instead of queueing a duplicate. Failed jobs are
            // NOT reused (failed work never re-runs — they want a fresh run),
            // and a different ConceptId is a deliberate re-index.
            var contentHash = await ComputeContentHashAsync(filePath, cancellationToken);
            var reusable = await FindReusableJobAsync(contentHash, request.ConceptId, cancellationToken);
            if (reusable is not null)
            {
                return ReuseResponse(reusable, filePath);
            }

            job = new TextbookJob
            {
                Id = jobId,
                FileName = request.FileName,
                ConceptId = request.ConceptId,
                ContentHash = contentHash,
                Status = TextbookJobStatus.Pending
            };
            _db.TextbookJobs.Add(job);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The unique (ContentHash, ConceptId) index caught the two-admin
                // race: an identical upload committed its row between our lookup
                // and this save. Re-look up the winning row and reuse it
                // instead of queueing a duplicate ingestion. An unrelated
                // DbUpdateException finds no winner here and rethrows below
                // (#104 CodeRabbit).
                var winner = await FindReusableJobAsync(contentHash, request.ConceptId, cancellationToken);
                if (winner is null)
                {
                    throw;
                }
                return ReuseResponse(winner, filePath);
            }
            jobSaved = true;

            _queue.Enqueue(jobId, filePath);
        }
        catch
        {
            // Enqueue failed after the row committed: end it as Failed so the
            // status endpoint can't poll a Pending job that will never run.
            // Deleting it would 404 a concurrent duplicate that the dedup path
            // already answered with this jobId; Failed stays pollable and a
            // re-upload starts fresh.
            if (jobSaved && job is not null)
            {
                try
                {
                    job.Status = TextbookJobStatus.Failed;
                    job.Error = "The ingestion queue was unavailable. Re-upload the file to retry.";
                    job.CompletedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(CancellationToken.None);
                }
                catch
                {
                    // best effort — preserve the original failure
                }
            }

            // Don't strand the staged file when anything after the write fails.
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // best effort — orphan cleanup beats masking the original error
            }

            throw;
        }

        var message = $"Background job started. Check GET /api/v1/admin/textbook/status/{jobId}.";
        return ApiResponse<UploadTextbookResultDto>.SuccessResult(
            new UploadTextbookResultDto(jobId, message),
            "Accepted");
    }

    private static async Task<string> ComputeContentHashAsync(
        string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var bytes = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(bytes);
    }

    // The dedup key: in-flight beats completed; most recent wins within a status.
    private async Task<TextbookJob?> FindReusableJobAsync(
        string contentHash, Guid? conceptId, CancellationToken cancellationToken)
    {
        var inFlight = await _db.TextbookJobs
            .Where(j => j.ContentHash == contentHash
                && j.ConceptId == conceptId
                && (j.Status == TextbookJobStatus.Pending
                    || j.Status == TextbookJobStatus.Processing))
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (inFlight is not null)
        {
            return inFlight;
        }

        return await _db.TextbookJobs
            .Where(j => j.ContentHash == contentHash
                && j.ConceptId == conceptId
                && j.Status == TextbookJobStatus.Completed)
            .OrderByDescending(j => j.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static ApiResponse<UploadTextbookResultDto> ReuseResponse(
        TextbookJob job, string stagedFile)
    {
        DeleteStagedFileBestEffort(stagedFile);
        var message = job.Status == TextbookJobStatus.Completed
            ? $"This file was already ingested. Check GET /api/v1/admin/textbook/status/{job.Id}."
            : $"An ingestion job for this file is already running. Check GET /api/v1/admin/textbook/status/{job.Id}.";
        return ApiResponse<UploadTextbookResultDto>.SuccessResult(
            new UploadTextbookResultDto(job.Id, message), "Accepted");
    }

    // ponytail: best effort — orphan cleanup beats masking the reuse result
    private static void DeleteStagedFileBestEffort(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch
        {
            // Ignore: the staged duplicate will be cleaned by the reuse path owner.
        }
    }
}
