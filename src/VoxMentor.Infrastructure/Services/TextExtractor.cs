using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using VoxMentor.Application.Common.Exceptions;

namespace VoxMentor.Infrastructure.Services;

/// <summary>Extracts plain text from uploaded .txt and .pdf files.</summary>
public static class TextExtractor
{
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromSeconds(30);

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
        // Client disconnects still propagate; a hung parse is stopped between
        // pages (PdfPig cannot be aborted mid-page) and surfaced as a 400.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ExtractionTimeout);
        var token = timeoutCts.Token;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                using var ms = new MemoryStream();
                await content.CopyToAsync(ms, token);
                var bytes = ms.ToArray();
                string decoded;
                try
                {
                    decoded = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('\uFEFF');
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
                return ExtractPdf(content, maxPages: 50, maxChars: 1_000_000, cancellationToken: token);
            }

            throw new NotSupportedException($"Unsupported file type '{extension}'.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = new[] { "Resume processing timed out." }
            });
        }
    }

    private static string ExtractPdf(Stream stream, int maxPages = int.MaxValue, int maxChars = int.MaxValue, CancellationToken cancellationToken = default)
    {
        using var document = PdfDocument.Open(stream);
        // Page objects are lightweight; text extraction (the memory-heavy step)
        // is lazy, so materializing the page list is cheap.
        var allPages = document.GetPages().ToList();
        if (allPages.Count > maxPages)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = new[] { $"Resume exceeds {maxPages} pages." }
            });

        var pages = new List<string>(allPages.Count);
        var total = 0;
        foreach (var page in allPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageText = ContentOrderTextExtractor.GetText(page);
            if (pageText.Length > maxChars - total)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["file"] = new[] { $"Resume exceeds {maxChars} characters." }
                });
            pages.Add(pageText);
            total += pageText.Length;
        }
        return string.Join("\n\n", pages);
    }
}
