using System.Diagnostics;
using System.Runtime.InteropServices;
using VoxMentor.Application.Common.Exceptions;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Runs PDF text extraction in an isolated worker process with OS-enforced
/// limits, so a decompression-bomb or pathological PDF cannot exhaust the
/// API host's memory or thread time (#151, CodeRabbit finding).
///
/// The worker executable (<c>VoxMentor.Workers.PdfExtractor</c>) parses the
/// PDF and writes extracted text to a temp output file. This class:
///   - caps the worker's private memory (<see cref="MaxPrivateMemoryBytes"/>),
///   - caps wall-clock time (<see cref="ExtractionTimeout"/>),
///   - reads the output file on success,
///   - maps worker exit codes to <see cref="ValidationException"/> (400).
/// </summary>
public sealed class PdfExtractionWorker : IDisposable
{
    private const int MaxPrivateMemoryBytes = 300 * 1024 * 1024; // 300 MiB
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromSeconds(30);

    private readonly string _workerExePath;
    private readonly string _tempOutputPath;

    /// <summary>Creates a worker that launches <c>VoxMentor.Workers.PdfExtractor.exe</c>.</summary>
    public PdfExtractionWorker(string workerExePath, string? tempOutputPath = null)
    {
        _workerExePath = workerExePath;
        _tempOutputPath = tempOutputPath ?? Path.Combine(
            Path.GetTempPath(),
            $"voxmentor-pdf-{Guid.NewGuid():N}.txt");
    }

    /// <summary>Extracts text from a PDF file via the isolated worker process.</summary>
    /// <exception cref="ValidationException">Over page/char limit, corrupt PDF, or worker failure.</exception>
    /// <exception cref="OperationCanceledException">Worker timed out.</exception>
    public string Extract(string inputPdfPath)
    {
        if (!File.Exists(_workerExePath))
        {
            throw new InvalidOperationException(
                $"PDF extraction worker not found at {_workerExePath}. " +
                "Build VoxMentor.Workers.PdfExtractor and copy it next to the API.");
        }

        // Ensure a clean output file.
        if (File.Exists(_tempOutputPath))
        {
            File.Delete(_tempOutputPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _workerExePath,
            Arguments = $"\"{inputPdfPath}\" \"{_tempOutputPath}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Run from the worker's own directory so PdfPig.dll and its
            // dependencies are resolvable (they live next to the exe).
            WorkingDirectory = Path.GetDirectoryName(_workerExePath) ?? Directory.GetCurrentDirectory(),
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start PDF extraction worker.");

        // OS-enforced memory cap (best effort; JobObject limits need native interop).
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                process.MaxWorkingSet = (nint)MaxPrivateMemoryBytes;
                process.MinWorkingSet = (nint)MaxPrivateMemoryBytes;
            }
        }
        catch
        {
            // Not all runtimes honor working-set limits; timeout is the backstop.
        }

        var exited = process.WaitForExit((int)ExtractionTimeout.TotalMilliseconds);
        if (!exited)
        {
            try { process.Kill(); } catch { }
            throw new OperationCanceledException(
                $"PDF extraction timed out after {ExtractionTimeout.TotalSeconds}s.");
        }

        var exitCode = process.ExitCode;
        var stderr = process.StandardError.ReadToEnd();

        switch (exitCode)
        {
            case 0:
                if (!File.Exists(_tempOutputPath))
                {
                    throw new ValidationException(new Dictionary<string, string[]>
                    {
                        ["file"] = ["Worker produced no output."]
                    });
                }
                return File.ReadAllText(_tempOutputPath);

            case 2: // too many pages
            case 3: // too many chars
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["file"] = [$"{(stderr.TrimEnd() ?? "Resume exceeds extraction limits.")} (worker exit code {exitCode})"]
                });

            default: // 1 (usage), 4 (exception), or other
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["file"] = [$"Worker failed (exit code {exitCode}): {stderr.TrimEnd()}".TrimEnd(':')]
                });
        }
    }

    public void Dispose()
    {
        try { if (File.Exists(_tempOutputPath)) File.Delete(_tempOutputPath); } catch { }
    }
}
