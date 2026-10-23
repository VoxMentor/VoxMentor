using System.Text.Json;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>
/// One ordered conversation turn persisted in MockInterview.TranscriptJson (#124).
/// Shared with complete/review (#125) and the interview hub (#126).
/// </summary>
public record TranscriptTurn(string Role, string Message, Guid? QuestionId, DateTime At)
{
    /// <summary>Turn author for the interviewer's generated lines.</summary>
    public const string InterviewerRole = "interviewer";

    /// <summary>Turn author for the candidate's submitted answers.</summary>
    public const string CandidateRole = "candidate";

    /// <summary>Single serializer both write and read so round-trips stay stable.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
