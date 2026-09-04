using FluentValidation;

namespace VoxMentor.Application.Features.Practice.SubmitAnswer;

/// <summary>
/// Validates <see cref="SubmitAnswerCommand"/> requests before they reach the handler.
/// </summary>
public class SubmitAnswerValidator : AbstractValidator<SubmitAnswerCommand>
{
    public SubmitAnswerValidator()
    {
        RuleFor(x => x.QuestionId)
            .NotEmpty().WithMessage("QuestionId is required.");
    }
}
