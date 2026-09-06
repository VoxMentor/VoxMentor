using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Executes source code against test cases via the Code Execution
/// microservice (Judge0-backed) and returns per-case results.
/// </summary>
public interface ICodeExecService
{
    /// <summary>
    /// Executes the given code once per stdin/expected-output test case and
    /// returns the execution results for every case.
    /// </summary>
    Task<IReadOnlyList<CodeExecutionCaseResult>> ExecuteAsync(
        CodeExecutionRequest request, CancellationToken cancellationToken = default);
}
