using VoxMentor.Application.Features.MockInterviews;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Generates mock-interview dialogue lines and rubric reviews via Ollama
/// (#124/#125). Every method returns null on any failure — callers degrade to
/// a canned line or canned review, never 500.
/// </summary>
public interface IMockInterviewGenerator
{
    /// <summary>Opening greeting + first problem, seeded by the JD's company/role.</summary>
    /// <param name="company">JD company name, or null when the interview has no JD.</param>
    /// <param name="role">JD role title, or null when the interview has no JD.</param>
    /// <param name="problemTitle">Title of the adaptively selected opening problem.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> OpeningAsync(
        string? company, string? role, string problemTitle, CancellationToken cancellationToken = default);

    /// <summary>One follow-up question or probe for the candidate's latest turn.</summary>
    /// <param name="transcript">Ordered conversation turns so far.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> FollowUpAsync(
        IReadOnlyList<TranscriptTurn> transcript, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rubric review of a finished transcript: overall score, one
    /// technicalReview entry per problem (keys problem1..problemN), and
    /// next-steps guidance. Null on any failure — the caller substitutes a
    /// canned review and skips mastery updates (#125).
    /// </summary>
    /// <param name="transcript">Ordered conversation turns of the run.</param>
    /// <param name="type">Interview format, which shifts the grading rubric.</param>
    /// <param name="company">JD company name, or null when the interview has no JD.</param>
    /// <param name="role">JD role title, or null when the interview has no JD.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MockInterviewReviewDraft?> ReviewAsync(
        IReadOnlyList<TranscriptTurn> transcript,
        InterviewType type,
        string? company,
        string? role,
        CancellationToken cancellationToken = default);
}
