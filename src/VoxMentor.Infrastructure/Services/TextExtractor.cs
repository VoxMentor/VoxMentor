using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>Extracts plain text from uploaded .txt and .pdf files.</summary>
/// <remarks>
/// Bounded so a single parse cannot exhaust memory or thread time:
/// 50 pages / 1M chars max, cancellation checked between pages.
/// Over-limit input throws <see cref="Application.Common.Exceptions.ValidationException"/>
/// (→ 400 via ExceptionHandlingMiddleware).
/// </remarks>
public static class TextExtractor
{
    /// <summary>Hard cap on characters retained per file (~1 MB of text).</summary>
    public const int MaxChars = 1_000_000;

    /// <summary>Hard cap on pages read before extraction aborts.</summary>
    public const int MaxPages = 50;

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
            return await ExtractTxt(stream, cancellationToken);
        }

        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractPdf(stream, cancellationToken);
        }

        throw new ValidationException(new Dictionary<string, string[]>
        {
            ["file"] = [$"Unsupported file type '{extension}'. Only .pdf is supported."]
        });
    }

    private static async Task<string> ExtractTxt(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);

        // Strict UTF-8 validation: StreamReader defaults to lenient replacement,
        // so re-decode from a buffered copy and reject invalid bytes.
        stream.Position = 0;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        string decoded;
        try
        {
            // strict mode throws on invalid UTF-8 sequences
            decoded = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = ["File is not valid UTF-8."]
            });
        }

        decoded = decoded.TrimStart('\uFEFF'); // leading BOM
        if (decoded.Any(c => char.IsControl(c) && c != '\t' && c != '\n' && c != '\r'))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = ["File contains binary content and cannot be read as text."]
            });
        }

        return decoded;
    }

    private static async Task<string> ExtractPdf(Stream stream, CancellationToken cancellationToken)
    {
        var pages = new List<string>();
        var totalChars = 0;
        using var document = PdfDocument.Open(stream);

        var pageCount = 0;
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++pageCount > MaxPages)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["file"] = [$"Resume exceeds {MaxPages} pages."]
                });
            }

            // ContentOrderTextExtractor keeps reading order; page.Text does not.
            var pageText = ContentOrderTextExtractor.GetText(page);
            totalChars += pageText.Length;
            if (totalChars > MaxChars)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["file"] = [$"Resume exceeds {MaxChars:N0} characters."]
                });
            }

            pages.Add(pageText);
        }

        return string.Join("\n\n", pages);
    }

    /// <summary>Wraps the static <see cref="TextExtractor"/> as <see cref="ITextExtractor"/> for DI.</summary>
    public sealed class TextExtractorAdapter : ITextExtractor
    {
        public Task<string> ExtractAsync(Stream stream, string extension, CancellationToken cancellationToken = default)
            => TextExtractor.ExtractAsync(stream, extension, cancellationToken);
    }
}
