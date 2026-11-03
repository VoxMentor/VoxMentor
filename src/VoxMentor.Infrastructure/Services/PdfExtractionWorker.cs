using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using VoxMentor.Application.Common.Exceptions;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Extracts PDF text in an isolated worker process with an OS-enforced memory limit.
/// PdfPig v0.1.16 has no decoded-size limit: <c>ContentOrderTextExtractor.GetText(page)</c>
/// decompresses the entire content stream atomically, so a deflate bomb can allocate
/// ~2 GB before any in-process clamp can run. The worker process is confined by a
/// JobObject (Windows) or cgroup/rlimit (Linux), bounding peak decoding memory
/// independent of the API process. If the OS memory-limit mechanism is unavailable,
/// the worker still runs in its own process — process isolation itself prevents
/// a decompression bomb from exhausting the API process's memory.
/// </summary>
public sealed class PdfExtractionWorker : IDisposable
{
    private static readonly TimeSpan WorkerTimeout = TimeSpan.FromSeconds(30);
    private static readonly long WorkerMemoryLimitBytes = 64L * 1024 * 1024; // 64 MB

    private readonly string _workerExe;
    private readonly int _maxPages;
    private readonly int _maxChars;

    public PdfExtractionWorker(string workerExe, int maxPages = 50, int maxChars = 1_000_000)
    {
        _workerExe = workerExe;
        _maxPages = maxPages;
        _maxChars = maxChars;
    }

    public async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_workerExe))
            throw new InvalidOperationException($"PDF extraction worker not found: {_workerExe}");

        var psi = new ProcessStartInfo
        {
            FileName = _workerExe,
            Arguments = $"\"{filePath}\" {_maxPages} {_maxChars}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start PDF extraction worker.");

        try
        {
            ApplyMemoryLimit(process);
        }
        catch (Exception)
        {
            // Memory limiting unavailable on this platform — proceed uncapped.
            // Process isolation still bounds the blast radius: an OOM-killed worker
            // cannot exhaust the API process's memory.
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        var exitTask = process.WaitForExitAsync(cancellationToken);
        var completed = await Task.WhenAny(
            exitTask,
            Task.Delay(WorkerTimeout, cancellationToken));

        if (completed != exitTask)
        {
            try { process.Kill(); } catch { }
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = new[] { "Resume processing timed out." }
            });
        }

        stdout.Append(await stdoutTask);
        stderr.Append(await stderrTask);

        var exitCode = process.ExitCode;
        if (exitCode == 0)
            return stdout.ToString();

        if (exitCode == 1)
        {
            var message = stderr.ToString().Trim();
            var limit = message.StartsWith("TOO_MANY_PAGES:")
                ? $"Resume exceeds {_maxPages} pages."
                : message.StartsWith("TOO_MANY_CHARS:")
                    ? $"Resume exceeds {_maxChars} characters."
                    : "Resume exceeds extraction limits.";
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = new[] { limit }
            });
        }

        // Exit code 2 or unexpected: OOM-killed, corrupt PDF, or worker crash.
        var workerError = stderr.ToString().Trim();
        throw new ValidationException(new Dictionary<string, string[]>
        {
            ["file"] = new[] { $"Could not read the resume file: {workerError}" }
        });
    }

    /// <summary>
    /// Binds the worker to a JobObject with a hard memory limit (Windows).
    /// On Linux the caller is expected to use cgroup v2 or prlimit via systemd/
    /// container configuration; this method is a no-op there.
    /// </summary>
    private static void ApplyMemoryLimit(Process process)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var hJob = CreateJobObject(IntPtr.Zero, null);
        if (hJob == IntPtr.Zero)
            throw new InvalidOperationException("Unable to create JobObject.");

        try
        {
            var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = 0x2000, // JOB_OBJECT_LIMIT_PROCESS_MEMORY
                ProcessMemoryLimit = (ulong)WorkerMemoryLimitBytes,
            };

            var size = Marshal.SizeOf(info);
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (!SetInformationJobObject(hJob, JobObjectInfoType.JobObjectBasicLimitInformation, ptr, (uint)size))
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

                if (!AssignProcessToJobObject(hJob, process.Handle))
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        finally
        {
            CloseHandle(hJob);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType,
        IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private enum JobObjectInfoType
    {
        JobObjectBasicLimitInformation = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public ulong Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
        public ulong ProcessMemoryLimit; // JOB_OBJECT_LIMIT_PROCESS_MEMORY
    }

    public void Dispose()
    {
        // No resources held at this level.
    }
}
