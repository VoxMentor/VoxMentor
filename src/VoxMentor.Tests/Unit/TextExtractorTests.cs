using System.Text;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Infrastructure.Services;
using Xunit;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="TextExtractor"/>: bounds, validation, and
/// cancellation. No worker process — parse is in-process PdfPig (#151).
/// </summary>
public class TextExtractorTests
{
    private static byte[] MinimalPdf(string text)
    {
        var escaped = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var content = $"BT /F1 12 Tf 72 720 Td ({escaped}) Tj ET";
        var objects = new List<string>
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n",
            $"4 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n",
            "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n",
        };

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        var offsets = new List<int>();
        foreach (var obj in objects)
        {
            offsets.Add(sb.Length);
            sb.Append(obj);
        }
        var xrefPos = sb.Length;
        sb.Append("xref\n");
        sb.Append($"0 {objects.Count + 1}\n");
        sb.Append("0000000000 65535 f \n");
        foreach (var off in offsets)
            sb.Append($"{off:D10} 00000 n \n");
        sb.Append("trailer\n");
        sb.Append($"<< /Size {objects.Count + 1} /Root 1 0 R >>\n");
        sb.Append("startxref\n");
        sb.Append($"{xrefPos}\n");
        sb.Append("%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Fact]
    public async Task ExtractAsync_Txt_ReturnsText()
    {
        var bytes = Encoding.UTF8.GetBytes("hello resume");
        using var stream = new MemoryStream(bytes);
        var result = await TextExtractor.ExtractAsync(stream, ".txt");
        Assert.Equal("hello resume", result);
    }

    [Fact]
    public async Task ExtractAsync_TxtWithBom_StripsBom()
    {
        var bytes = Encoding.UTF8.GetBytes("\uFEFFresume");
        using var stream = new MemoryStream(bytes);
        var result = await TextExtractor.ExtractAsync(stream, ".txt");
        Assert.Equal("resume", result);
    }

    [Fact]
    public async Task ExtractAsync_BinaryTxt_ThrowsValidation()
    {
        var bytes = new byte[] { 0xFF, 0x00, 0x01, 0x02 };
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<ValidationException>(() =>
            TextExtractor.ExtractAsync(stream, ".txt"));
    }

    [Fact]
    public async Task ExtractAsync_InvalidUtf8Txt_ThrowsValidation()
    {
        // 0xC3 0x28 is an incomplete UTF-8 sequence.
        var bytes = new byte[] { 0x48, 0x65, 0xC3, 0x28 }; // "He" + invalid
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<ValidationException>(() =>
            TextExtractor.ExtractAsync(stream, ".txt"));
    }

    [Fact]
    public async Task ExtractAsync_UnsupportedExtension_ThrowsValidation()
    {
        var bytes = Encoding.UTF8.GetBytes("not a resume");
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<ValidationException>(() =>
            TextExtractor.ExtractAsync(stream, ".docx"));
    }

    [Fact]
    public async Task ExtractAsync_Pdf_ReturnsText()
    {
        var bytes = MinimalPdf("Python developer");
        using var stream = new MemoryStream(bytes);
        var result = await TextExtractor.ExtractAsync(stream, ".pdf");
        Assert.Contains("Python", result);
    }

    [Fact]
    public async Task ExtractAsync_PdfOverCharLimit_ThrowsValidation()
    {
        var bytes = MinimalPdf(new string('a', 1_000_001));
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<ValidationException>(() =>
            TextExtractor.ExtractAsync(stream, ".pdf"));
    }

    [Fact]
    public async Task ExtractAsync_CorruptPdf_Throws()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            TextExtractor.ExtractAsync(stream, ".pdf"));
    }

    [Fact]
    public async Task ExtractAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var bytes = MinimalPdf("Python developer");
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            TextExtractor.ExtractAsync(stream, ".pdf", cts.Token));
    }
}
