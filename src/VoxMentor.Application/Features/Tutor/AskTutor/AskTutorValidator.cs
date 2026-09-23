using FluentValidation;

namespace VoxMentor.Application.Features.Tutor.AskTutor;

/// <summary>Validates AskTutorCommand before it reaches the handler.</summary>
public class AskTutorValidator : AbstractValidator<AskTutorCommand>
{
    public AskTutorValidator()
    {
        RuleFor(x => x.Question)
            .NotEmpty().WithMessage("Question is required.")
            .MaximumLength(2000).WithMessage("Question must be 2000 characters or fewer.");
    }
}
