using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Admin.GetQuestions;

/// <summary>
/// Retrieves a paginated list of questions with concept and test case counts.
/// Uses AsNoTracking for read-only performance.
/// </summary>
public class GetQuestionsHandler : IRequestHandler<GetQuestionsQuery, ApiResponse<GetQuestionsResultDto>>
{
    private readonly IApplicationDbContext _db;

    public GetQuestionsHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ApiResponse<GetQuestionsResultDto>> Handle(
        GetQuestionsQuery request, CancellationToken cancellationToken)
    {
        // ponytail: manual page size cap instead of a separate validator (GET queries skip FluentValidation pipeline)
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(1, request.Page);

        var baseQuery = _db.Questions.AsNoTracking();
        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var questions = await baseQuery
            .OrderByDescending(q => q.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(q => new QuestionDto(
                q.Id,
                q.ConceptId,
                q.Title,
                q.Description,
                q.Difficulty,
                q.TestCases.Length,
                q.CreatedAt))
            .ToListAsync(cancellationToken);

        return ApiResponse<GetQuestionsResultDto>.SuccessResult(
            new GetQuestionsResultDto(questions, totalCount, page, pageSize));
    }
}
