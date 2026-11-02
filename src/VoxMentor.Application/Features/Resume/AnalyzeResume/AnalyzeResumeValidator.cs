using FluentValidation;

namespace VoxMentor.Application.Features.Resume.AnalyzeResume;

/// <summary>Validates AnalyzeResumeCommand before the handler runs.</summary>
public sealed class AnalyzeResumeValidator : AbstractValidator<AnalyzeResumeCommand>
{
    /// <summary>ponytail: 5 MB is plenty for a resume; raise if real files overflow.</summary>
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;

    private static readonly string[] AllowedExtensions = { ".pdf", ".txt" };

    public AnalyzeResumeValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("File name is required.")
            .MaximumLength(260).WithMessage("File name must not exceed 260 characters.")
            .Must(f => AllowedExtensions.Contains(
                Path.GetExtension(f)?.ToLowerInvariant() ?? string.Empty,
                StringComparer.Ordinal))
            .WithMessage("Only .pdf and .txt files are supported.");

        RuleFor(x => x.Content)
            .NotNull().WithMessage("File content is required.");

        RuleFor(x => x.Length)
            .GreaterThan(0).WithMessage("File must not be empty.")
            .LessThanOrEqualTo(MaxFileSizeBytes)
            .WithMessage($"File must be {MaxFileSizeBytes} bytes or smaller.");

        RuleFor(x => x.JdId)
            .NotEmpty().WithMessage("JdId is required.");
    }
}
