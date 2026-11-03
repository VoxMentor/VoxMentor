using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace VoxMentor.Workers.PdfExtractor;

/// <summary>
/// Standalone PDF extraction worker process.
///
/// Invoked by <see cref="PdfExtractionWorker"/> with:
///   args[0] = input PDF file path
///   args[1] = output text file path
///
/// Parses the PDF in an isolated process so a decompression-bomb or
/// pathological PDF cannot exhaust the API's memory. The caller enforces
/// an OS memory limit on the process; if PdfPig runs away parsing a dense
/// page, the worker is terminated before it can starve the host.
///
/// Writes extracted text to the output file, or nothing on failure so the
/// caller can surface a clean 400.
/// </summary>
public static class Program
{
    private const int MaxPages = 50;
    private const int MaxChars = 1_000_000;

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: VoxMentor.Workers.PdfExtractor <input-pdf> <output-txt>");
            return 1;
        }

        var inputPath = args[0];
        var outputPath = args[1];

        try
        {
            using var document = PdfDocument.Open(inputPath);

            // Fail fast on page count before parsing any page content.
            var totalPages = document.GetPages().Count();
            if (totalPages > MaxPages)
            {
                Console.Error.WriteLine($"Resume exceeds {MaxPages} pages.");
                return 2;
            }

            var pages = new List<string>();
            var totalChars = 0;
            var pageCount = 0;
            foreach (var page in document.GetPages())
            {
                if (++pageCount > MaxPages)
                {
                    break;
                }

                // ContentOrderTextExtractor keeps reading order; page.Text does not.
                var pageText = ContentOrderTextExtractor.GetText(page);
                totalChars += pageText.Length;
                if (totalChars > MaxChars)
                {
                    Console.Error.WriteLine($"Resume exceeds {MaxChars:N0} characters.");
                    return 3;
                }

                pages.Add(pageText);
            }

            File.WriteAllText(outputPath, string.Join("\n\n", pages));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to extract PDF: {ex}");
            return 4;
        }
    }
}
