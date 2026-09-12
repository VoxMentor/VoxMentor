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

        RuleFor(x => x.TestCases)
            .NotEmpty().WithMessage("At least one test case is required.");
    }
}
