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

    public async Task<IReadOnlyList<CodeExecutionCaseResult>> ExecuteAsync(
        CodeExecutionRequest request, CancellationToken cancellationToken = default)
    {
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

            using var response = await _http.SendAsync(requestMessage, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException(
                    "The code execution service rejected the forwarded credentials.");
            }
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<ExecuteResponse>(JsonOptions, cancellationToken);
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
