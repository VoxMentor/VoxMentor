using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Features.Admin.CreateQuestion;

/// <summary>
/// Handles question creation: validates concept exists, persists the question,
/// and returns the created question metadata.
/// </summary>
public class CreateQuestionHandler : IRequestHandler<CreateQuestionCommand, ApiResponse<CreateQuestionResultDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateQuestionHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <exception cref="UnauthorizedAccessException">No authenticated user.</exception>
    /// <exception cref="NotFoundException">The concept does not exist.</exception>
    public async Task<ApiResponse<CreateQuestionResultDto>> Handle(
        CreateQuestionCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User must be authenticated to create a question.");

        var conceptExists = await _db.Concepts
            .AnyAsync(c => c.Id == request.ConceptId, cancellationToken);

        if (!conceptExists)
            throw new NotFoundException($"Concept {request.ConceptId} was not found.");

        var testCases = TestCasePayload.Parse(request.TestCases);
        if (testCases.Errors.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]> { ["TestCases"] = [.. testCases.Errors] });

        var rubric = RubricPayload.Parse(request.Rubric);
        if (!rubric.IsValid)
            throw new ValidationException(new Dictionary<string, string[]> { ["Rubric"] = [.. rubric.Errors] });

        var question = new Question
        {
            Id = Guid.NewGuid(),
            ConceptId = request.ConceptId,
            Title = request.Title,
            Description = request.Description,
            QuestionType = request.QuestionType,
            Difficulty = request.Difficulty,
            TestCases = request.TestCases,
            Rubric = request.Rubric ?? Array.Empty<string>(),
            ExampleInputs = request.ExampleInputs ?? Array.Empty<string>(),
            ExampleOutputs = request.ExampleOutputs ?? Array.Empty<string>(),
            StarterCode = request.StarterCode ?? Array.Empty<string>(),
            HiddenTestCaseCount = testCases.HiddenCount
        };

        _db.Questions.Add(question);
        await _db.SaveChangesAsync(cancellationToken);

        return ApiResponse<CreateQuestionResultDto>.SuccessResult(
            new CreateQuestionResultDto(question.Id, question.ConceptId, question.Title, question.Difficulty),
            "Question created successfully.");
    }
}
