namespace VoxMentor.Application.Common.Models;

/// <summary>
/// A request to execute source code against a set of test cases via the
/// Code Execution microservice.
/// </summary>
/// <param name="Code">Source code to execute.</param>
/// <param name="LanguageId">Judge0 language identifier (e.g. 71 = Python).</param>
/// <param name="TestCases">Test cases to run; each supplies stdin and the expected output.</param>
public record CodeExecutionRequest(
    string Code,
    int LanguageId,
    IReadOnlyList<CodeExecutionTestCase> TestCases);

/// <summary>A single test case: the stdin fed to the program and the expected output.</summary>
public record CodeExecutionTestCase(string Stdin, string ExpectedOutput);

/// <summary>
/// Execution outcome for one test case, as reported by the Code Execution
/// microservice.
/// </summary>
public record CodeExecutionCaseResult(
    string Stdin,
    string Expected,
    string Actual,
    bool Passed,
    bool TimedOut,
    string Status,
    string Stderr,
    int? ExecutionTimeMs,
    int? MemoryUsageKb);
