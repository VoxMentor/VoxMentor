using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetQuestions;

/// <summary>
/// Retrieves a paginated list of questions with optional concept/difficulty
/// filters. Joins concepts for the name. Read-only, no tracking.
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
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(1, request.Page);

        var baseQuery = _db.Questions
            .AsNoTracking()
            .AsQueryable();

        if (request.ConceptId.HasValue)
            baseQuery = baseQuery.Where(q => q.ConceptId == request.ConceptId.Value);

        if (request.Difficulty.HasValue)
            baseQuery = baseQuery.Where(q => q.Difficulty == request.Difficulty.Value);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue)
            return ApiResponse<GetQuestionsResultDto>.SuccessResult(
                new GetQuestionsResultDto(new List<StudentQuestionDto>(), totalCount, page, pageSize));

        var questions = await baseQuery
            .OrderBy(q => q.Difficulty)
            .ThenBy(q => q.Title)
            .Skip((int)offset)
            .Take(pageSize)
            .Join(
                _db.Concepts.AsNoTracking(),
                q => q.ConceptId,
                c => c.Id,
                (q, c) => new StudentQuestionDto(
                    q.Id,
                    q.ConceptId,
                    c.Name,
                    q.Title,
                    q.Description,
                    q.QuestionType,
                    q.Difficulty,
                    q.ExampleInputs.Length,
                    q.TestCases.Length,
                    q.HiddenTestCaseCount))
            .ToListAsync(cancellationToken);

        return ApiResponse<GetQuestionsResultDto>.SuccessResult(
            new GetQuestionsResultDto(questions, totalCount, page, pageSize));
    }
}
