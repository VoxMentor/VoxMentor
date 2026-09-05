using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.Practice.SubmitCode;

/// <summary>
/// Processes code submissions end-to-end: authenticates the user, loads the
/// question and its test cases, executes each case via the Code Execution
/// service (forwarding the user's token), runs the AI evaluator, persists the
/// <see cref="CodeSubmission"/> row, derives correctness, applies the BKT
/// mastery update, and publishes a mastery-updated event. Concurrent mastery
/// writes are handled with bounded optimistic-concurrency retries.
/// </summary>
public class SubmitCodeHandler : IRequestHandler<SubmitCodeCommand, ApiResponse<SubmitCodeResultDto>>
{
    /// <summary>Delimiter separating stdin and expected output inside a question test case.</summary>
    public const string TestCaseDelimiter = "|";

    private static readonly JsonSerializerOptions EvaluationJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IReadOnlyDictionary<string, int> LanguageIds =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["python"] = 71,
            ["java"] = 62,
            ["cpp"] = 54,
            ["c"] = 50,
            ["javascript"] = 63,
            ["csharp"] = 51
        };

    private readonly IApplicationDbContext _db;
    private readonly IBktEngine _bktEngine;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTokenService _currentToken;
    private readonly ICodeExecService _codeExecService;
    private readonly ICodeEvaluator _codeEvaluator;
    private readonly IMasteryEventPublisher _eventPublisher;
    private readonly ILogger<SubmitCodeHandler> _logger;

    public SubmitCodeHandler(
        IApplicationDbContext db,
        IBktEngine bktEngine,
        ICurrentUserService currentUser,
        ICurrentTokenService currentToken,
        ICodeExecService codeExecService,
        ICodeEvaluator codeEvaluator,
        IMasteryEventPublisher eventPublisher,
        ILogger<SubmitCodeHandler> logger)
    {
        _db = db;
        _bktEngine = bktEngine;
        _currentUser = currentUser;
        _currentToken = currentToken;
        _codeExecService = codeExecService;
        _codeEvaluator = codeEvaluator;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <exception cref="UnauthorizedAccessException">No authenticated user.</exception>
    /// <exception cref="NotFoundException">The question does not exist.</exception>
    public async Task<ApiResponse<SubmitCodeResultDto>> Handle(SubmitCodeCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            throw new UnauthorizedAccessException("User must be authenticated to submit code.");
        }

        var question = await _db.Questions
            .FirstOrDefaultAsync(q => q.Id == request.QuestionId, cancellationToken);
        if (question is null)
        {
            throw new NotFoundException($"Question {request.QuestionId} was not found.");
        }

        var testCases = ParseTestCases(question.TestCases);
        var executionResults = testCases.Count > 0
            ? await _codeExecService.ExecuteAsync(
                new CodeExecutionRequest(request.Code, LanguageIds[request.Language], testCases),
                cancellationToken)
            : [];

        var passedCount = executionResults.Count(r => r.Passed);
        var status = DeriveStatus(executionResults, testCases.Count);
        bool? isCorrect = status == SubmissionStatus.Accepted
            ? (bool?)true
            : testCases.Count > 0 ? false : null;

        // AI evaluation is supplementary: a failed evaluator must not fail the
        // submission, so failures are logged and stored as null.
        CodeEvaluation? evaluation = null;
        try
        {
            evaluation = await _codeEvaluator.EvaluateAsync(request.Code, request.Language, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AI evaluation failed for submission to question {QuestionId}", request.QuestionId);
        }

        var submission = new CodeSubmission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            QuestionId = question.Id,
            Code = request.Code,
            Language = request.Language,
            IsCorrect = isCorrect ?? false,
            TestCasesPassed = passedCount,
            TestCasesTotal = testCases.Count,
            ExecutionTimeMs = executionResults.Count != 0
                ? (int?)executionResults.Sum(r => r.ExecutionTimeMs ?? 0)
                : null,
            MemoryUsageKb = executionResults.Count != 0
                ? executionResults.Max(r => r.MemoryUsageKb ?? 0) is { } kb && kb > 0 ? kb : null
                : null,
            AiEvaluation = evaluation is not null
                ? JsonSerializer.Serialize(evaluation, EvaluationJsonOptions)
                : null,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
        _db.CodeSubmissions.Add(submission);

        float previousMastery;
        float newMastery;
        StudentMastery? updatedMastery = null;
        if (isCorrect is null)
        {
            (previousMastery, newMastery) =
                await GetCurrentMasteryAsync(userId, question.ConceptId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            var bkt = await ApplyBktUpdateAsync(userId, question.ConceptId, isCorrect.Value, submission, cancellationToken);
            previousMastery = bkt.Previous;
            newMastery = bkt.New;
            updatedMastery = bkt.Mastery;
        }

        // Publish mastery event AFTER save to prevent stale data on subscribers when
        // the transaction rolls back. Matches the SubmitAnswerHandler pattern.
        if (updatedMastery is not null)
        {
            await _eventPublisher.PublishMasteryUpdatedAsync(updatedMastery, previousMastery, cancellationToken);
        }

        var result = new SubmitCodeResultDto(
            submission.Id,
            status.ToString(),
            isCorrect,
            passedCount,
            testCases.Count,
            submission.ExecutionTimeMs,
            submission.MemoryUsageKb,
            evaluation,
            [.. executionResults
                .Take(testCases.Count - question.HiddenTestCaseCount)
                .Select((r, i) => new CodeTestCaseResultDto(i, r.Stdin, r.Expected, r.Actual, r.Passed))],
            previousMastery,
            newMastery,
            newMastery - previousMastery);

        return ApiResponse<SubmitCodeResultDto>.SuccessResult(result, "Code submitted successfully.");
    }

    /// <summary>
    /// Parses the question's raw test cases. Each entry is
    /// "<c>stdin|expected</c>"; entries without the delimiter are treated as
    /// output-only cases (empty stdin).
    /// </summary>
    public static IReadOnlyList<CodeExecutionTestCase> ParseTestCases(string[] testCases)
    {
        var parsed = new List<CodeExecutionTestCase>(testCases.Length);
        foreach (var testCase in testCases)
        {
            var separatorIndex = testCase.IndexOf(TestCaseDelimiter, StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                parsed.Add(new CodeExecutionTestCase(string.Empty, testCase));
            }
            else
            {
                parsed.Add(new CodeExecutionTestCase(
                    testCase[..separatorIndex],
                    testCase[(separatorIndex + 1)..]));
            }
        }
        return parsed;
    }

    private static SubmissionStatus DeriveStatus(
        IReadOnlyList<CodeExecutionCaseResult> results, int totalCases)
    {
        if (totalCases == 0)
            return SubmissionStatus.Pending;
        if (results.Count == 0)
            return SubmissionStatus.RuntimeError;
        if (results.Any(r => r.TimedOut))
            return SubmissionStatus.Timeout;
        if (results.All(r => r.Passed))
            return SubmissionStatus.Accepted;
        return results.All(r => !r.Passed && r.Stderr.Length > 0)
            ? SubmissionStatus.RuntimeError
            : SubmissionStatus.WrongAnswer;
    }

    /// <summary>Result of a BKT mastery update.</summary>
    private sealed record BktResult(float Previous, float New, StudentMastery Mastery);

    /// <summary>Applies the BKT mastery update and persists both the submission
    /// and mastery atomically, with bounded retries on write races (same
    /// semantics as the answer-submission pipeline).</summary>
    private async Task<BktResult> ApplyBktUpdateAsync(
        string userId, Guid conceptId, bool isCorrect, CodeSubmission submission, CancellationToken cancellationToken)
    {
        var parameters = await _db.BktParameters
            .FirstOrDefaultAsync(p => p.ConceptId == conceptId, cancellationToken)
            ?? new BktParameters { ConceptId = conceptId };

        const int maxAttempts = 3;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var mastery = await _db.StudentMasteries
                    .FirstOrDefaultAsync(m => m.UserId == userId && m.ConceptId == conceptId, cancellationToken);
                if (mastery is null)
                {
                    mastery = new StudentMastery
                    {
                        UserId = userId,
                        ConceptId = conceptId,
                        MasteryProbability = parameters.PriorKnowledge
                    };
                    _db.StudentMasteries.Add(mastery);
                }

                var previousMastery = mastery.MasteryProbability;
                var newMastery = _bktEngine.UpdateMastery(previousMastery, parameters, isCorrect);

                mastery.MasteryProbability = newMastery;
                if (isCorrect)
                    mastery.CorrectAttempts++;
                else
                    mastery.IncorrectAttempts++;
                mastery.LastPracticedAt = DateTime.UtcNow;
                mastery.UpdatedAt = DateTime.UtcNow;

                // Re-attach submission after ClearChangeTracker on retry.
                if (_db.Entry(submission).State == EntityState.Detached)
                    _db.CodeSubmissions.Add(submission);

                await _db.SaveChangesAsync(cancellationToken);
                return new BktResult(previousMastery, newMastery, mastery);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts - 1)
            {
                _db.ClearChangeTracker();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConflictException("Concurrent submissions conflicted. Please retry.");
            }
            catch (DbUpdateException) when (attempt < maxAttempts - 1)
            {
                _db.ClearChangeTracker();
                var existing = await _db.StudentMasteries
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.UserId == userId && m.ConceptId == conceptId, cancellationToken);
                if (existing is null)
                    throw;
            }
        }
    }

    /// <summary>
    /// Reads the current mastery for the concept without modifying it (used
    /// when no test cases ran and no BKT update should be applied).
    /// </summary>
    private async Task<(float Previous, float New)> GetCurrentMasteryAsync(
        string userId, Guid conceptId, CancellationToken cancellationToken)
    {
        var mastery = await _db.StudentMasteries
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.ConceptId == conceptId, cancellationToken);
        var value = mastery?.MasteryProbability ?? 0f;
        return (value, value);
    }
}
