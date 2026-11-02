using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using VoxMentor.Application.Common.Exceptions;

namespace VoxMentor.Infrastructure.Services;

/// <summary>Extracts plain text from uploaded .txt and .pdf files.</summary>
public static class TextExtractor
{
    public static async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return await File.ReadAllTextAsync(filePath, cancellationToken);
        }

        if (filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = File.OpenRead(filePath);
            return ExtractPdf(stream, cancellationToken: cancellationToken);
        }

        throw new NotSupportedException($"Unsupported file type '{Path.GetExtension(filePath)}'.");
    }

    public static async Task<string> ExtractAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();
            string decoded;
            try
            {
                decoded = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["file"] = new[] { "Resume text must be valid UTF-8." }
                });
            }

            // Reject binary content masquerading as text.
            if (decoded.Any(c => char.IsControl(c) && c != '\t' && c != '\n' && c != '\r'))
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["file"] = new[] { "Resume file appears to be binary, not text." }
                });
            }
            return decoded;
        }

        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            // Resume path: bounded extraction (50 pages / 1 MB) to limit memory.
            return ExtractPdf(content, maxPages: 50, maxChars: 1_000_000, cancellationToken: cancellationToken);
        }

        throw new NotSupportedException($"Unsupported file type '{extension}'.");
    }

    private static string ExtractPdf(Stream stream, int maxPages = int.MaxValue, int maxChars = int.MaxValue, CancellationToken cancellationToken = default)
    {
        var pages = new List<string>();
        using var document = PdfDocument.Open(stream);
        // ponytail: caps bound the resume path only; the ingestion path (path
        // overload) stays unlimited so textbook chunks are never silently dropped.
        // A single pathological page can still blow memory before the cap — a
        // full fix needs a parse timeout, add if real uploads cause issues.
        var total = 0;
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pages.Count >= maxPages || total >= maxChars) break;
            var pageText = ContentOrderTextExtractor.GetText(page);
            if (pageText.Length > maxChars - total)
                pageText = pageText[..(maxChars - total)];
            pages.Add(pageText);
            total += pageText.Length;
        }
        return string.Join("\n\n", pages);
    }
}
