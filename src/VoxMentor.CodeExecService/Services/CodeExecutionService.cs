using System.Globalization;
using VoxMentor.CodeExecService.Clients;
using VoxMentor.CodeExecService.Models;

namespace VoxMentor.CodeExecService.Services;

public class CodeExecutionService
{
    private readonly Judge0Client _judge0Client;

    private static readonly HashSet<int> SupportedLanguages = new()
    {
        71,  // Python
        62,  // Java
        54,  // C++
        63,  // JavaScript
        51   // C#
    };

    /// <summary>Initializes the service with the Judge0 execution client.</summary>
    public CodeExecutionService(Judge0Client judge0Client)
    {
        _judge0Client = judge0Client;
    }

    /// <summary>Whether the Judge0 language id is in the supported set.</summary>
    public bool IsLanguageSupported(int languageId) => SupportedLanguages.Contains(languageId);

    /// <summary>
    /// Executes the request via Judge0 and maps the result: stdout/stderr (with
    /// compile-output fallback), invariant-culture time parsing, timeout flag,
    /// and strict per-test-case output comparison when expected outputs are given.
    /// </summary>
    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var judge0Result = await _judge0Client.ExecuteAsync(
            request.Code, request.LanguageId, request.Stdin ?? string.Empty, cancellationToken);

        var result = new ExecutionResult
        {
            Stdout = judge0Result.Stdout ?? string.Empty,
            Stderr = judge0Result.Stderr ?? judge0Result.CompileOutput ?? string.Empty,
            ExecutionTimeMs = judge0Result.Time != null
                && double.TryParse(judge0Result.Time, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? (int)(seconds * 1000)
                : null,
            MemoryUsageKb = judge0Result.Memory,
            Status = judge0Result.Status?.Description ?? "Unknown",
            TimedOut = judge0Result.Status?.Id == 5
        };

        if (request.ExpectedOutputs is { Length: > 0 })
        {
            result.TestResults = new List<TestCaseResult>();
            var actualOutputs = NormalizeOutput(result.Stdout);

            for (int i = 0; i < request.ExpectedOutputs.Length; i++)
            {
                var expected = request.ExpectedOutputs[i];
                var actual = i < actualOutputs.Length ? actualOutputs[i] : null;

                result.TestResults.Add(new TestCaseResult
                {
                    TestCaseIndex = i,
                    Expected = expected,
                    Actual = actual ?? string.Empty,
                    Passed = actual != null
                        && string.Equals(expected, actual, StringComparison.Ordinal)
                });
            }

            result.AllTestCasesPassed = result.TestResults.All(t => t.Passed)
                && actualOutputs.Length == request.ExpectedOutputs.Length;
        }

        return result;
    }

    /// <summary>
    /// Splits output into lines, removing at most one final line terminator while
    /// preserving interior blank lines and additional trailing newlines that are
    /// part of the expected output (e.g. "ok\n\n" becomes ["ok", ""]. A single
    /// bare newline ("\n") is one empty output line; empty input is zero lines.
    /// </summary>
    private static string[] NormalizeOutput(string output)
    {
        var normalized = output.Replace("\r\n", "\n");
        var hadNewline = normalized.EndsWith('\n');
        if (hadNewline)
            normalized = normalized[..^1];

        if (normalized.Length == 0)
            return hadNewline ? new[] { string.Empty } : Array.Empty<string>();

        return normalized.Split('\n');
    }
}
