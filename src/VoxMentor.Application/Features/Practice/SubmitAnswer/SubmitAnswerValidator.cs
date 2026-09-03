using FluentValidation;

namespace VoxMentor.Application.Features.Practice.SubmitAnswer;

public class SubmitAnswerValidator : AbstractValidator<SubmitAnswerCommand>
{
    public SubmitAnswerValidator()
    {
        RuleFor(x => x.QuestionId)
            .NotEmpty().WithMessage("QuestionId is required.");
    }
}
