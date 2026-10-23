using VoxMentor.Application.Features.MockInterviews;

namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Generates mock-interview dialogue lines via Ollama (#124). Every method
/// returns null on any failure â€” callers degrade to a canned line, never 500.
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
}
