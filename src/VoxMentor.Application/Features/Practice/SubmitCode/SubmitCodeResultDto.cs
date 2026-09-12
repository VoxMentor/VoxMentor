using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.SubmitCode;

/// <summary>
/// Result of a code submission: execution outcome per test case, AI
/// evaluation, and the BKT mastery transition for the question's concept.
/// </summary>
/// <param name="SubmissionId">Id of the persisted <c>CodeSubmission</c> row.</param>
/// <param name="Status">Execution status (Accepted, WrongAnswer, Timeout, RuntimeError, or Pending).</param>
/// <param name="IsCorrect">True when every test case passed; null when there were no test cases to compare.</param>
/// <param name="TestCasesPassed">Number of test cases that passed.</param>
/// <param name="TestCasesTotal">Total number of test cases executed.</param>
/// <param name="ExecutionTimeMs">Total execution time in milliseconds across cases.</param>
/// <param name="MemoryUsageKb">Peak memory usage in kilobytes across cases.</param>
/// <param name="AiEvaluation">AI evaluation of the code, or null when unavailable.</param>
/// <param name="TestCaseResults">Per-test-case outcomes (expected vs actual).</param>
/// <param name="PreviousMastery">Concept mastery before this submission.</param>
/// <param name="NewMastery">Concept mastery after the BKT update.</param>
/// <param name="MasteryDelta">Mastery change: NewMastery - PreviousMastery.</param>
public record SubmitCodeResultDto(
    Guid SubmissionId,
    string Status,
    bool? IsCorrect,
    int TestCasesPassed,
    int TestCasesTotal,
    int? ExecutionTimeMs,
    int? MemoryUsageKb,
    CodeEvaluation? AiEvaluation,
    IReadOnlyList<CodeTestCaseResultDto> TestCaseResults,
    float PreviousMastery,
    float NewMastery,
    float MasteryDelta);

/// <summary>Outcome of a single executed test case.</summary>
public record CodeTestCaseResultDto(
    int TestCaseIndex,
    string Stdin,
    string Expected,
    string Actual,
    bool Passed);
