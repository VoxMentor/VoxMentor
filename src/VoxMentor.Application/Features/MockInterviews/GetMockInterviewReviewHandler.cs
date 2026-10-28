using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>
/// GET /api/v1/mock/{interviewId}/review (#125): returns the stored review.
/// 404 for unknown/foreign ids (IUserOwned filter) and for interviews that
/// have not completed yet.
/// </summary>
public class GetMockInterviewReviewHandler : IRequestHandler<GetMockInterviewReviewQuery, ApiResponse<GetMockInterviewReviewResultDto>>
{
    private readonly IApplicationDbContext _db;

    /// <summary>Initializes the handler with the data context.</summary>
    public GetMockInterviewReviewHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>Loads the completed interview and its persisted review.</summary>
    public async Task<ApiResponse<GetMockInterviewReviewResultDto>> Handle(
        GetMockInterviewReviewQuery request, CancellationToken cancellationToken)
    {
        var interview = await _db.MockInterviews
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.InterviewId, cancellationToken)
            ?? throw new NotFoundException("Interview not found.");

        // Not completed yet (or Failed) has no review — 404 per the issue.
        if (interview.Status != MockInterviewStatus.Completed || string.IsNullOrEmpty(interview.ReviewJson))
            throw new NotFoundException("Review not found: the interview has not been completed.");

        var review = JsonSerializer.Deserialize<MockInterviewReview>(
            interview.ReviewJson, TranscriptTurn.JsonOptions)
            ?? throw new NotFoundException("Review not found: the interview has not been completed.");

        return ApiResponse<GetMockInterviewReviewResultDto>.SuccessResult(
            new GetMockInterviewReviewResultDto(
                interview.Id,
                interview.Status.ToString(),
                interview.Score,
                review,
                interview.StartedAt,
                interview.CompletedAt),
            "Review retrieved successfully.");
    }
}
