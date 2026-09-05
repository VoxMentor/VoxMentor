using FluentValidation;

namespace VoxMentor.Application.Features.Practice.SubmitCode;

/// <summary>
/// Validates code submissions: the language must be one of the supported
/// set and the code must be present and within the size limit.
/// </summary>
public class SubmitCodeValidator : AbstractValidator<SubmitCodeCommand>
{
    /// <summary>Languages supported by the code-execution pipeline.</summary>
    public static readonly IReadOnlySet<string> SupportedLanguages =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "python", "java", "cpp", "javascript", "csharp"
        };

    private const int MaxCodeLength = 50_000;

    public SubmitCodeValidator()
    {
        RuleFor(x => x.QuestionId)
            .NotEmpty().WithMessage("QuestionId is required.");

        RuleFor(x => x.Language)
            .NotEmpty().WithMessage("Language is required.")
            .Must(language => SupportedLanguages.Contains(language))
            .WithMessage("Language must be one of: python, java, cpp, javascript, csharp.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Code is required.")
            .MaximumLength(MaxCodeLength)
            .WithMessage($"Code must not exceed {MaxCodeLength} characters.");
    }
}
