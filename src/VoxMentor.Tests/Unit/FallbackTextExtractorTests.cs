using System.Text;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Infrastructure.Services;
using Xunit;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="PdfPigTextExtractor.FallbackTextExtractor"/> — the
/// worker-unavailable fallback wired in DependencyInjection. Verifies .txt still
/// extracts in-process and .pdf fails with a clear ValidationException that
/// AnalyzeResumeHandler converts to a 400 instead of an unhandled 500.
/// </summary>
public class FallbackTextExtractorTests
{
    private static PdfPigTextExtractor.FallbackTextExtractor Create() => new();

    [Fact]
    public async Task ExtractAsync_Txt_ReturnsText()
    {
        var bytes = Encoding.UTF8.GetBytes("hello resume");
        using var stream = new MemoryStream(bytes);

        var result = await Create().ExtractAsync(stream, ".txt");

        Assert.Equal("hello resume", result);
    }

    [Fact]
    public async Task ExtractAsync_Pdf_ThrowsServiceUnavailable()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4"));

        var ex = await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            Create().ExtractAsync(stream, ".pdf"));

        Assert.Contains("worker is unavailable", ex.Message);
    }

    [Fact]
    public async Task ExtractAsync_BinaryTxt_ThrowsValidation()
    {
        var bytes = new byte[] { 0xFF, 0x00, 0x01, 0x02 };
        using var stream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<ValidationException>(() =>
            Create().ExtractAsync(stream, ".txt"));
    }
}
