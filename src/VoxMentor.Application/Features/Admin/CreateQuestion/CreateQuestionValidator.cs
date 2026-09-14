using FluentValidation;

namespace VoxMentor.Application.Features.Admin.CreateQuestion;

/// <summary>
/// Validates <see cref="CreateQuestionCommand"/> before it reaches the handler.
/// </summary>
public class CreateQuestionValidator : AbstractValidator<CreateQuestionCommand>
{
    public CreateQuestionValidator()
    {
        RuleFor(x => x.ConceptId)
            .NotEmpty().WithMessage("ConceptId is required.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(300).WithMessage("Title must not exceed 300 characters.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.");

        RuleFor(x => x.Difficulty)
            .InclusiveBetween(1, 10).WithMessage("Difficulty must be between 1 and 10.");

        RuleFor(x => x.QuestionType)
            .NotEmpty().WithMessage("QuestionType is required.")
            .MaximumLength(50).WithMessage("QuestionType must not exceed 50 characters.");

        RuleFor(x => x.TestCases)
            .NotEmpty().WithMessage("At least one test case is required.");

        RuleFor(x => x.TestCases)
            .Must(testCases => TestCasePayload.Parse(testCases).Errors.Count == 0)
            .WithMessage(x => string.Join(" ", TestCasePayload.Parse(x.TestCases).Errors));

        RuleFor(x => x.Rubric!)
            .Must(rubric => RubricPayload.Parse(rubric).IsValid)
            .When(x => x.Rubric is not null)
            .WithMessage(x => string.Join(" ", RubricPayload.Parse(x.Rubric).Errors));
    }
}
