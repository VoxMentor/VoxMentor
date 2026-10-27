using FluentValidation;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>Validates StartMockInterviewCommand before it reaches the handler (#124).</summary>
public class StartMockInterviewValidator : AbstractValidator<StartMockInterviewCommand>
{
    /// <summary>Initializes the validator: blank defaults later; otherwise a named, defined <see cref="InterviewType"/> only.</summary>
    public StartMockInterviewValidator()
    {
        RuleFor(x => x.Type)
            // Names only: Enum.TryParse accepts numeric strings ("2") for
            // undefined members, which would persist a bogus type (CodeRabbit #146).
            .Must(type => string.IsNullOrWhiteSpace(type) ||
                          Enum.GetNames<InterviewType>().Contains(type.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Type must be one of: Technical, Behavioral.");
    }
}
