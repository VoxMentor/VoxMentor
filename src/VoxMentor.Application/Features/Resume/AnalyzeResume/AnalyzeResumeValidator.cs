using FluentValidation;

namespace VoxMentor.Application.Features.Resume.AnalyzeResume;

/// <summary>Validates AnalyzeResumeCommand before it reaches the handler.</summary>
/// <remarks>ponytail: 5 MB is plenty for a resume; raise if real files overflow.</remarks>
public class AnalyzeResumeValidator : AbstractValidator<AnalyzeResumeCommand>
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;

    private static readonly string[] AllowedExtensions = { ".pdf" };

    public AnalyzeResumeValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("File name is required.")
            .MaximumLength(260).WithMessage("File name must not exceed 260 characters.")
            .Must(f => AllowedExtensions.Contains(
                System.IO.Path.GetExtension(f)?.ToLowerInvariant() ?? string.Empty,
                StringComparer.Ordinal))
            .WithMessage("Only .pdf files are supported.");

        RuleFor(x => x.Content)
            .NotNull().WithMessage("File content is required.");

        RuleFor(x => x.Length)
            .GreaterThan(0).WithMessage("File must not be empty.")
            .LessThanOrEqualTo(MaxFileSizeBytes)
            .WithMessage($"File must be {MaxFileSizeBytes} bytes or smaller.");

        RuleFor(x => x.JdId)
            .NotEmpty().WithMessage("jdId is required.")
            .NotEqual(Guid.Empty).WithMessage("jdId must be a valid GUID.");
    }
}
