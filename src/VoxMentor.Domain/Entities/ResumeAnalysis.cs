using VoxMentor.Domain.Interfaces;

namespace VoxMentor.Domain.Entities;

/// <summary>One persisted resume analysis row (#151). Owned by the analyzing user.</summary>
public class ResumeAnalysis : IUserOwned
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid JdId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int MatchScore { get; set; }
    public string KeywordMatchJson { get; set; } = string.Empty;
    public string AtsParseabilityJson { get; set; } = string.Empty;
    public string SuggestionsJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
