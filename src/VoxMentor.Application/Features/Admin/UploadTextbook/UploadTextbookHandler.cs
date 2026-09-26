using MediatR;
using Microsoft.EntityFrameworkCore;
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

        try
        {
            await using (request.Content)
            {
                await using var fileStream = File.Create(filePath);
                await request.Content.CopyToAsync(fileStream, cancellationToken);
            }

            var job = new TextbookJob
            {
                Id = jobId,
                FileName = request.FileName,
                ConceptId = request.ConceptId,
                Status = TextbookJobStatus.Pending
            };
            _db.TextbookJobs.Add(job);
            await _db.SaveChangesAsync(cancellationToken);

            _queue.Enqueue(jobId, filePath);
        }
        catch
        {
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
}
