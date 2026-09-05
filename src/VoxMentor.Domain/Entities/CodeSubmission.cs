using VoxMentor.Domain.Enums;

namespace VoxMentor.Domain.Entities;

/// <summary>
/// Represents a student's code submission for a practice question,
/// including execution results, AI evaluation, and status.
/// </summary>
public class CodeSubmission
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>The student who submitted the code.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>The question this submission targets.</summary>
    public Guid QuestionId { get; set; }

    /// <summary>The submitted source code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Programming language of the submission.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Whether all test cases passed.</summary>
    public bool IsCorrect { get; set; }

    /// <summary>Number of test cases that passed.</summary>
    public int TestCasesPassed { get; set; }

    /// <summary>Total number of test cases executed.</summary>
    public int TestCasesTotal { get; set; }

    /// <summary>Execution time in milliseconds (max across cases).</summary>
    public int? ExecutionTimeMs { get; set; }

    /// <summary>Peak memory usage in kilobytes.</summary>
    public int? MemoryUsageKb { get; set; }

    /// <summary>Plagiarism detection score (0-1), or null if not checked.</summary>
    public float? PlagiarismScore { get; set; }

    /// <summary>AI evaluation JSON from Ollama, or null if unavailable.</summary>
    public string? AiEvaluation { get; set; }

    /// <summary>Overall execution status.</summary>
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Pending;

    /// <summary>UTC timestamp when the submission was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
