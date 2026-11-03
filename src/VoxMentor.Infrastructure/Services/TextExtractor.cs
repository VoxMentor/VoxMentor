using System.IO;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>Extracts plain text from uploaded .txt and .pdf files.</summary>
/// <remarks>
/// PDFs are parsed in an isolated worker process (<see cref="PdfPigTextExtractor"/>,
/// backed by <see cref="PdfExtractionWorker"/>) with OS-enforced memory and
/// time limits (#151, CodeRabbit finding). TXT files are extracted in-process.
/// </remarks>
public static class TextExtractor
{
    /// <summary>Hard cap on characters retained per file (~1 MB of text).</summary>
    public const int MaxChars = 1_000_000;

    /// <summary>Hard cap on pages read before extraction aborts.</summary>
    public const int MaxPages = 50;

    private static readonly PdfPigTextExtractor _extractor = new();

    public static async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        await using var stream = File.OpenRead(filePath);
        return await ExtractAsync(stream, extension, cancellationToken);
    }

    /// <summary>Extracts text from an in-memory stream given its extension.</summary>
    public static async Task<string> ExtractAsync(Stream stream, string extension, CancellationToken cancellationToken = default)
    {
        return await _extractor.ExtractAsync(stream, extension, cancellationToken);
    }
}
