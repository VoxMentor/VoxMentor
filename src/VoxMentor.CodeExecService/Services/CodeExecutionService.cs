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

    public CodeExecutionService(Judge0Client judge0Client)
    {
        _judge0Client = judge0Client;
    }

    public bool IsLanguageSupported(int languageId) => SupportedLanguages.Contains(languageId);

    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var judge0Result = await _judge0Client.ExecuteAsync(
            request.Code, request.LanguageId, request.Stdin ?? string.Empty, cancellationToken);

        var result = new ExecutionResult
        {
            Stdout = judge0Result.Stdout ?? string.Empty,
            Stderr = judge0Result.Stderr ?? string.Empty,
            ExecutionTimeMs = judge0Result.Time != null
                ? (int)(double.Parse(judge0Result.Time) * 1000)
                : null,
            MemoryUsageKb = judge0Result.Memory,
            Status = judge0Result.Status?.Description ?? "Unknown",
            TimedOut = judge0Result.Status?.Id == 5
        };

        if (request.ExpectedOutputs is { Length: > 0 })
        {
            result.TestResults = new List<TestCaseResult>();
            var actualOutputs = result.Stdout.Split(new[] { '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < request.ExpectedOutputs.Length; i++)
            {
                var expected = request.ExpectedOutputs[i].Trim();
                var actual = i < actualOutputs.Length ? actualOutputs[i].Trim() : string.Empty;

                result.TestResults.Add(new TestCaseResult
                {
                    TestCaseIndex = i,
                    Expected = expected,
                    Actual = actual,
                    Passed = string.Equals(expected, actual, StringComparison.Ordinal)
                });
            }

            result.AllTestCasesPassed = result.TestResults.All(t => t.Passed);
        }

        return result;
    }
}
