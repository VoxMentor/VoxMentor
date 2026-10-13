using FluentValidation;

namespace VoxMentor.Application.Features.Jd.UploadJd;

/// <summary>Validates UploadJdCommand before it reaches the handler (#117).</summary>
public class UploadJdValidator : AbstractValidator<UploadJdCommand>
{
    /// <summary>Minimum useful JD length — below this there is nothing to parse.</summary>
    public const int MinRawTextLength = 50;

    /// <summary>Upper bound keeps a single request from monopolizing Ollama.</summary>
    public const int MaxRawTextLength = 20000;

    public UploadJdValidator()
    {
        RuleFor(x => x.RawText)
            .NotEmpty().WithMessage("Job description text is required.")
            .MinimumLength(MinRawTextLength)
            .WithMessage($"Job description must be at least {MinRawTextLength} characters.")
            .MaximumLength(MaxRawTextLength)
            .WithMessage($"Job description must not exceed {MaxRawTextLength} characters.");
    }
}
