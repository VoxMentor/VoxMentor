using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>ITextExtractor implementation backed by the in-process PdfPig extractor.</summary>
public sealed class PdfPigTextExtractor : ITextExtractor
{
    public Task<string> ExtractAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        => TextExtractor.ExtractAsync(content, extension, cancellationToken);
}
