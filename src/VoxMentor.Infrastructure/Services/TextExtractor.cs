using System.Diagnostics;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using VoxMentor.Application.Common.Exceptions;

namespace VoxMentor.Infrastructure.Services;

/// <summary>Extracts plain text from uploaded .txt and .pdf files.</summary>
public static class TextExtractor
{
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromSeconds(30);

    // 6 MB: matches the controller's [RequestSizeLimit]. Fail-fast before PdfPig
    // opens the stream so an oversized upload is rejected without decompressing.
    private static readonly long MaxInputStreamBytes = 6L * 1024 * 1024;

    public static async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return await File.ReadAllTextAsync(filePath, cancellationToken);
        }

        if (filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractPdf(filePath, cancellationToken: cancellationToken);
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
                // Copy to a temp file so the worker process can open it by path.
                // The 6 MB cap below bounds compressed input before PdfPig decompresses.
                var tempPath = Path.GetTempFileName();
                try
                {
                    await using (var fs = File.Create(tempPath))
                    {
                        await content.CopyToAsync(fs, token);
                    }
                    return ExtractPdf(tempPath, cancellationToken: token);
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { }
                }
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

    private static string ExtractPdf(string filePath, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(filePath);

        // Fail fast on oversized input — bounds compressed bytes before PdfPig
        // decompresses. Mirrors the controller's [RequestSizeLimit(6 MB)].
        if (stream.Length > MaxInputStreamBytes)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = new[] { $"Resume file exceeds {MaxInputStreamBytes / (1024 * 1024)} MB." }
            });

        // Try the isolated worker first (memory-bounded). Fall back to in-process
        // if the worker binary is unavailable (e.g. not published in dev).
        var workerExe = Path.Combine(
            AppContext.BaseDirectory,
            "VoxMentor.Workers.PdfExtractor.exe");

        if (File.Exists(workerExe))
        {
            using var worker = new PdfExtractionWorker(workerExe, maxPages: 50, maxChars: 1_000_000);
            return worker.ExtractAsync(filePath, cancellationToken).GetAwaiter().GetResult();
        }

        // ponytail: in-process fallback — no OS memory limit on decompressed text.
        // PdfPig v0.1.16 has no decoded-size cap; a deflate bomb can allocate ~2 GB.
        return ExtractPdfInProcess(stream, maxPages: 50, maxChars: 1_000_000,
            cancellationToken: cancellationToken);
    }

    private static string ExtractPdfInProcess(Stream stream, int maxPages = int.MaxValue,
        int maxChars = int.MaxValue, CancellationToken cancellationToken = default)
    {
        using var document = PdfDocument.Open(stream);

        if (document.NumberOfPages > maxPages)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = new[] { $"Resume exceeds {maxPages} pages." }
            });

        var pages = new List<string>(document.NumberOfPages);
        var total = 0;
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageText = ContentOrderTextExtractor.GetText(page);
            // Page separators count toward the budget so 50 pages × 20k chars
            // can't slip past 1,000,000 by 98 chars of "\n\n" padding.
            var separatorLength = pages.Count > 0 ? "\n\n".Length : 0;
            if (pageText.Length + separatorLength > maxChars - total)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["file"] = new[] { $"Resume exceeds {maxChars} characters." }
                });
            pages.Add(pageText);
            total += separatorLength + pageText.Length;
        }
        return string.Join("\n\n", pages);
    }
}
