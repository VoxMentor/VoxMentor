using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

// Usage: PdfExtractorWorker <filePath> <maxPages> <maxChars>
// Outputs extracted text to stdout; writes structured errors to stderr.
// Exit codes: 0 = success, 1 = validation limit exceeded, 2 = unexpected error.
if (args.Length != 3)
{
    Console.Error.WriteLine("USAGE: PdfExtractorWorker <filePath> <maxPages> <maxChars>");
    Environment.Exit(2);
    return;
}

var filePath = args[0];
if (!int.TryParse(args[1], out var maxPages) || maxPages <= 0)
{
    Console.Error.WriteLine("INVALID_ARG:maxPages");
    Environment.Exit(2);
    return;
}
if (!int.TryParse(args[2], out var maxChars) || maxChars <= 0)
{
    Console.Error.WriteLine("INVALID_ARG:maxChars");
    Environment.Exit(2);
    return;
}

try
{
    using var document = PdfDocument.Open(filePath);

    if (document.NumberOfPages > maxPages)
    {
        Console.Error.WriteLine($"TOO_MANY_PAGES:{maxPages}");
        Environment.Exit(1);
        return;
    }

    var pages = new List<string>(document.NumberOfPages);
    var total = 0;
    foreach (var page in document.GetPages())
    {
        var pageText = ContentOrderTextExtractor.GetText(page);
        var separatorLength = pages.Count > 0 ? "\n\n".Length : 0;
        if (pageText.Length + separatorLength > maxChars - total)
        {
            Console.Error.WriteLine($"TOO_MANY_CHARS:{maxChars}");
            Environment.Exit(1);
            return;
        }
        pages.Add(pageText);
        total += separatorLength + pageText.Length;
    }

    Console.Write(string.Join("\n\n", pages));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"EXTRACT_ERROR:{ex.Message}");
    Environment.Exit(2);
}
