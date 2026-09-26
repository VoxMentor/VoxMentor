using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

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
            return ExtractPdf(filePath);
        }

        throw new NotSupportedException($"Unsupported file type '{Path.GetExtension(filePath)}'.");
    }

    private static string ExtractPdf(string filePath)
    {
        var pages = new List<string>();
        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
        {
            // ContentOrderTextExtractor keeps reading order; page.Text does not.
            pages.Add(ContentOrderTextExtractor.GetText(page));
        }
        return string.Join("\n\n", pages);
    }
}
