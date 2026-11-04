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
///   - caps wall-clock time (<see cref="ExtractionTimeout"/>),
///   - honors request cancellation by killing the worker process,
///   - reads the output file on success,
///   - maps worker exit codes to <see cref="ValidationException"/> (400).
/// </summary>
public sealed class PdfExtractionWorker : IDisposable
{
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromSeconds(30);

    private readonly string _workerExePath;

    /// <summary>Creates a worker that launches <c>VoxMentor.Workers.PdfExtractor.exe</c>.</summary>
    public PdfExtractionWorker(string workerExePath)
    {
        _workerExePath = workerExePath;
    }

    /// <summary>Extracts text from a PDF file via the isolated worker process.</summary>
    /// <param name="cancellationToken">Request cancellation token; cancelling
    /// kills the worker process.</param>
    /// <exception cref="ValidationException">Over page/char limit, corrupt PDF, or worker failure.</exception>
    /// <exception cref="OperationCanceledException">Request was cancelled or worker timed out.</exception>
    public async Task<string> ExtractAsync(
        string inputPdfPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_workerExePath))
        {
            throw new InvalidOperationException(
                $"PDF extraction worker not found at {_workerExePath}. " +
                "Build VoxMentor.Workers.PdfExtractor and copy it next to the API.");
        }

        // Unique output path per call so concurrent analyses don't collide.
        var tempOutputPath = Path.Combine(
            Path.GetTempPath(),
            $"voxmentor-pdf-out-{Guid.NewGuid():N}.txt");

        var startInfo = new ProcessStartInfo
        {
            FileName = _workerExePath,
            Arguments = $"\"{inputPdfPath}\" \"{tempOutputPath}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Run from the worker's own directory so PdfPig.dll and its
            // dependencies are resolvable (they live next to the exe).
            WorkingDirectory = Path.GetDirectoryName(_workerExePath) ?? Directory.GetCurrentDirectory(),
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start PDF extraction worker.");

        // Async wait so the request thread isn't blocked; a client disconnect
        // or the 30s timeout cancels the wait and kills the worker.
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ExtractionTimeout);
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { }
            // Client cancellation → rethrow; our timeout → clean 400.
            cancellationToken.ThrowIfCancellationRequested();
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = [$"PDF extraction timed out after {ExtractionTimeout.TotalSeconds}s."]
            });
        }

        try
        {
            // Cancellation after the wait but before reading results must not
            // be misreported as an invalid PDF (#151, CodeRabbit finding).
            cancellationToken.ThrowIfCancellationRequested();

            var exitCode = process.ExitCode;
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

            if (exitCode != 0)
            {
                var reason = exitCode is 2 or 3
                    ? $"{(string.IsNullOrWhiteSpace(stderr) ? "Resume exceeds extraction limits." : stderr.TrimEnd())} (worker exit code {exitCode})"
                    : $"Worker failed (exit code {exitCode}): {stderr.TrimEnd()}".TrimEnd(':');
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["file"] = [reason]
                });
            }

            if (!File.Exists(tempOutputPath))
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["file"] = ["Worker produced no output."]
                });
            }

            return await File.ReadAllTextAsync(tempOutputPath, cancellationToken);
        }
        finally
        {
            // Delete on every exit path — success, worker failure, timeout,
            // cancellation — so extracted resume text never lingers in temp.
            try { File.Delete(tempOutputPath); } catch { }
        }
    }

    public void Dispose()
    {
    }
}
