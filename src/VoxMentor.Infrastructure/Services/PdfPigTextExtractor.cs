using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// <see cref="ITextExtractor"/> implementation that parses PDFs in an
/// isolated worker process with OS-enforced memory and time limits
/// (#151, CodeRabbit finding). TXT files are extracted in-process.
/// </summary>
public class PdfPigTextExtractor : ITextExtractor
{
    private readonly PdfExtractionWorker _worker;

    /// <summary>
    /// Creates the extractor. The worker executable is located by searching
    /// known locations: alongside the running assembly (production deployments
    /// where it's copied via build targets) and in the worker project's own
    /// output directory (development/test, where PdfPig.dll also lives).
    /// </summary>
    public PdfPigTextExtractor()
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "VoxMentor.Workers.PdfExtractor.exe"
            : "VoxMentor.Workers.PdfExtractor";

        var candidatePaths = new[]
        {
            // Worker project's own output directory (development/test): PdfPig.dll lives here.
            Path.Combine(AppContext.BaseDirectory, "..", "VoxMentor.Workers.PdfExtractor", "bin", "Release", "net8.0", exeName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "VoxMentor.Workers.PdfExtractor", "bin", "Release", "net8.0", exeName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "VoxMentor.Workers.PdfExtractor", "bin", "Release", "net8.0", exeName),
            // Alongside the running assembly (production deployments where both exe
            // and PdfPig.dll are copied via build targets).
            Path.Combine(AppContext.BaseDirectory, exeName),
        };

        string? workerExe = null;
        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                workerExe = path;
                break;
            }
        }

        _workerExePath = workerExe ?? throw new InvalidOperationException(
            $"PDF extraction worker '{exeName}' not found. Searched: {string.Join("; ", candidatePaths)}");
        _worker = new PdfExtractionWorker(_workerExePath);
    }

    private readonly string _workerExePath;

    /// <exception cref="ValidationException">Unsupported type, invalid UTF-8, binary content,
    /// over-limit file, or worker failure.</exception>
    public async Task<string> ExtractAsync(Stream stream, string extension, CancellationToken cancellationToken = default)
    {
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractTxt(stream, cancellationToken);
        }

        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractPdfViaWorker(stream, cancellationToken);
        }

        throw new ValidationException(new Dictionary<string, string[]>
        {
            ["file"] = [$"Unsupported file type '{extension}'. Only .pdf is supported."]
        });
    }

    internal static async Task<string> ExtractTxt(Stream stream, CancellationToken cancellationToken)
    {
        // Buffer once; reuse for strict UTF-8 decode and char-limit check.
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        string decoded;
        try
        {
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

        if (decoded.Length > TextExtractor.MaxChars)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = [$"Resume exceeds {TextExtractor.MaxChars:N0} characters."]
            });
        }

        return decoded;
    }

    private async Task<string> ExtractPdfViaWorker(Stream stream, CancellationToken cancellationToken)
    {
        // Check cancellation before any async work so we throw
        // OperationCanceledException (not TaskCanceledException from CopyToAsync).
        cancellationToken.ThrowIfCancellationRequested();

        // The worker operates on file paths, so stage the uploaded stream to a
        // temp file before launching the isolated process. Close and dispose
        // the file stream before the worker reads it (Windows file sharing).
        var tempInput = Path.Combine(Path.GetTempPath(), $"voxmentor-pdf-in-{Guid.NewGuid():N}.pdf");
        try
        {
            using (var fileStream = File.Create(tempInput))
            {
                await stream.CopyToAsync(fileStream, cancellationToken);
            }

            return await _worker.ExtractAsync(tempInput, cancellationToken);
        }
        finally
        {
            try { if (File.Exists(tempInput)) File.Delete(tempInput); } catch { }
        }
    }

    /// <summary>
    /// Fallback <see cref="ITextExtractor"/> used when the PDF worker cannot be
    /// constructed (worker exe missing). TXT files still extract in-process;
    /// PDFs throw a <see cref="ValidationException"/> that
    /// <see cref="AnalyzeResumeHandler"/> converts to a 400 instead of an
    /// unhandled DI-resolution 500.
    /// </summary>
    internal sealed class FallbackTextExtractor : ITextExtractor
    {
        public Task<string> ExtractAsync(Stream stream, string extension, CancellationToken cancellationToken = default)
        {
            if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                return PdfPigTextExtractor.ExtractTxt(stream, cancellationToken);
            }

            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = ["PDF extraction worker is unavailable — .pdf resumes cannot be processed."]
            });
        }
    }
}
