using FluentValidation;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>Validates AnswerMockInterviewCommand before it reaches the handler (#124).</summary>
public class AnswerMockInterviewValidator : AbstractValidator<AnswerMockInterviewCommand>
{
    /// <summary>Initializes the validator: an answer message is required (API.md Â§6 â†’ 400).</summary>
    public AnswerMockInterviewValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Answer message is required.");
    }
}
