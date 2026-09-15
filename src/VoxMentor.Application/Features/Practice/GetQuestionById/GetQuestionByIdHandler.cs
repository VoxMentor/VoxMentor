using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetQuestionById;

/// <summary>
/// Fetches a single question by ID with hidden test cases stripped.
/// Returns examples, starter code, and visible test cases only.
/// </summary>
public class GetQuestionByIdHandler : IRequestHandler<GetQuestionByIdQuery, ApiResponse<QuestionDetailDto>>
{
    private readonly IApplicationDbContext _db;

    public GetQuestionByIdHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ApiResponse<QuestionDetailDto>> Handle(
        GetQuestionByIdQuery request, CancellationToken cancellationToken)
    {
        var question = await _db.Questions
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == request.Id, cancellationToken);

        if (question is null)
            throw new NotFoundException($"Question {request.Id} was not found.");

        var conceptName = await _db.Concepts
            .AsNoTracking()
            .Where(c => c.Id == question.ConceptId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var visibleTestCases = question.HiddenTestCaseCount > 0
            ? question.TestCases[..^question.HiddenTestCaseCount]
            : question.TestCases;

        var dto = new QuestionDetailDto(
            question.Id,
            question.ConceptId,
            conceptName,
            question.Title,
            question.Description,
            question.QuestionType,
            question.Difficulty,
            question.ExampleInputs,
            question.ExampleOutputs,
            question.StarterCode,
            visibleTestCases,
            question.Rubric,
            question.TestCases.Length,
            question.HiddenTestCaseCount);

        return ApiResponse<QuestionDetailDto>.SuccessResult(dto);
    }
}
