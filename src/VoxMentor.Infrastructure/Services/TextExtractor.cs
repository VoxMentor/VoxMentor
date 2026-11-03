using System.IO;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>Extracts plain text from uploaded .txt and .pdf files.</summary>
/// <remarks>
/// PDFs are parsed in an isolated worker process (<see cref="PdfPigTextExtractor"/>,
/// backed by <see cref="PdfExtractionWorker"/>) with OS-enforced memory and
/// time limits (#151, CodeRabbit finding). TXT files are extracted in-process
/// without constructing the PDF extractor.
/// </remarks>
public static class TextExtractor
{
    /// <summary>Hard cap on characters retained per file (~1 MB of text).</summary>
    public const int MaxChars = 1_000_000;

    /// <summary>Hard cap on pages read before extraction aborts.</summary>
    public const int MaxPages = 50;

    /// <summary>Lazily creates the PDF extractor so TXT-only paths never touch it.</summary>
    private static readonly Lazy<PdfPigTextExtractor> _pdfExtractor =
        new(() => new PdfPigTextExtractor());

    public static async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        await using var stream = File.OpenRead(filePath);
        return await ExtractAsync(stream, extension, cancellationToken);
    }

    /// <summary>Extracts text from an in-memory stream given its extension.</summary>
    public static async Task<string> ExtractAsync(Stream stream, string extension, CancellationToken cancellationToken = default)
    {
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return await PdfPigTextExtractor.ExtractTxt(stream, cancellationToken);
        }

        return await _pdfExtractor.Value.ExtractAsync(stream, extension, cancellationToken);
    }
}
