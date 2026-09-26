using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Admin.GetTextbookJob;

/// <summary>Loads a TextbookJob by id, or 404s.</summary>
public class GetTextbookJobHandler : IRequestHandler<GetTextbookJobQuery, ApiResponse<TextbookJobDto>>
{
    private readonly IApplicationDbContext _db;

    public GetTextbookJobHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ApiResponse<TextbookJobDto>> Handle(
        GetTextbookJobQuery request, CancellationToken cancellationToken)
    {
        var job = await _db.TextbookJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);
        if (job is null)
        {
            throw new NotFoundException($"Textbook job {request.JobId} was not found.");
        }

        var dto = new TextbookJobDto(
            job.Id,
            job.FileName,
            job.Status,
            job.TotalChunks,
            job.ProcessedChunks,
            job.Error,
            job.CreatedAt,
            job.CompletedAt);
        return ApiResponse<TextbookJobDto>.SuccessResult(dto);
    }
}
