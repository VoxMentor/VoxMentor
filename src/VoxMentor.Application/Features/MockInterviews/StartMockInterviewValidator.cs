using FluentValidation;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>Validates StartMockInterviewCommand before it reaches the handler (#124).</summary>
public class StartMockInterviewValidator : AbstractValidator<StartMockInterviewCommand>
{
    /// <summary>Initializes the validator: type must name an <see cref="InterviewType"/> when supplied.</summary>
    public StartMockInterviewValidator()
    {
        RuleFor(x => x.Type)
            .Must(type => string.IsNullOrWhiteSpace(type) ||
                          Enum.TryParse<InterviewType>(type, ignoreCase: true, out _))
            .WithMessage("Type must be one of: Technical, Behavioral.");
    }
}
