using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Typed client for the Code Execution microservice: runs each test case via
/// <c>POST /api/v1/execute</c> (Judge0-backed), forwarding the user's access
/// token so the service's Student-role authorization applies to the original
/// submitter rather than the service itself.
/// </summary>
public class CodeExecServiceClient : ICodeExecService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly ICurrentTokenService _currentToken;
    private readonly ILogger<CodeExecServiceClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Initialises a new instance of the <see cref="CodeExecServiceClient"/> class.</summary>
    /// <param name="http">The HTTP client used to call the Code Execution microservice.</param>
    /// <param name="config">Application configuration providing the service base URL.</param>
    /// <param name="currentToken">Provides the user's access token for forwarding.</param>
    /// <param name="logger">Logger for execution diagnostics.</param>
    public CodeExecServiceClient(
        HttpClient http,
        IConfiguration config,
        ICurrentTokenService currentToken,
        ILogger<CodeExecServiceClient> logger)
    {
        _http = http;
        _baseUrl = (config["CodeExecService:BaseUrl"] ?? "http://localhost:5001").TrimEnd('/');
        _currentToken = currentToken;
        _logger = logger;
    }

    /// <summary>
    /// Executes the code against each test case by calling the Code Execution
    /// microservice. A linked 60-second timeout caps total execution time
    /// regardless of how many test cases are provided.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">
    /// The service rejected the forwarded credentials.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The service returned an empty response body.
    /// </exception>
    public async Task<IReadOnlyList<CodeExecutionCaseResult>> ExecuteAsync(
        CodeExecutionRequest request, CancellationToken cancellationToken = default)
    {
        // Cap total execution time across all test cases to prevent a single
        // submission with many cases from monopolising CodeExecService capacity.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

        var results = new List<CodeExecutionCaseResult>(request.TestCases.Count);

        foreach (var testCase in request.TestCases)
        {
            var payload = new
            {
                code = request.Code,
                languageId = request.LanguageId,
                stdin = testCase.Stdin,
                expectedOutputs = new[] { testCase.ExpectedOutput }
            };

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/v1/execute")
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            if (_currentToken.AccessToken is { Length: > 0 } token)
            {
                requestMessage.Headers.Authorization = new("Bearer", token);
            }

            using var response = await _http.SendAsync(requestMessage, timeoutCts.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException(
                    "The code execution service rejected the forwarded credentials.");
            }
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<ExecuteResponse>(JsonOptions, timeoutCts.Token);
            if (body is null)
            {
                throw new InvalidOperationException("The code execution service returned an empty response.");
            }

            results.Add(ToCaseResult(testCase.Stdin, body));
            _logger.LogInformation(
                "Executed case {Index}/{Total} for language {LanguageId}: status {Status}",
                results.Count, request.TestCases.Count, request.LanguageId, body.Status);
        }

        return results;
    }

    /// <summary>Maps the service response to a <see cref="CodeExecutionCaseResult"/>.</summary>
    private static CodeExecutionCaseResult ToCaseResult(string stdin, ExecuteResponse body)
    {
        var caseResult = body.TestResults is { Count: > 0 } ? body.TestResults[0] : null;
        return new CodeExecutionCaseResult(
            stdin,
            caseResult?.Expected ?? string.Empty,
            caseResult?.Actual ?? body.Stdout,
            caseResult?.Passed ?? false,
            body.TimedOut,
            body.Status,
            body.Stderr,
            body.ExecutionTimeMs,
            body.MemoryUsageKb);
    }

    /// <summary>JSON response from the Code Execution microservice.</summary>
    private sealed class ExecuteResponse
    {
        [JsonPropertyName("stdout")]
        public string Stdout { get; set; } = string.Empty;

        [JsonPropertyName("stderr")]
        public string Stderr { get; set; } = string.Empty;

        [JsonPropertyName("executionTimeMs")]
        public int? ExecutionTimeMs { get; set; }

        [JsonPropertyName("memoryUsageKb")]
        public int? MemoryUsageKb { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("timedOut")]
        public bool TimedOut { get; set; }

        [JsonPropertyName("allTestCasesPassed")]
        public bool AllTestCasesPassed { get; set; }

        [JsonPropertyName("testResults")]
        public List<ExecuteTestCaseResult>? TestResults { get; set; }
    }

    /// <summary>Per-test-case result from the Code Execution microservice.</summary>
    private sealed class ExecuteTestCaseResult
    {
        [JsonPropertyName("testCaseIndex")]
        public int TestCaseIndex { get; set; }

        [JsonPropertyName("expected")]
        public string Expected { get; set; } = string.Empty;

        [JsonPropertyName("actual")]
        public string Actual { get; set; } = string.Empty;

        [JsonPropertyName("passed")]
        public bool Passed { get; set; }
    }
}
