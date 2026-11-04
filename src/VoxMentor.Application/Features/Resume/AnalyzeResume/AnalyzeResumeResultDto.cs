using System.Text.Json.Serialization;

namespace VoxMentor.Application.Features.Resume.AnalyzeResume;

/// <summary>Fit report returned by POST /api/v1/resume/analyze (#151).</summary>
public class AnalyzeResumeResultDto
{
    /// <summary>Generated per-analysis id (echoes the persisted ResumeAnalysis row).</summary>
    public Guid AnalysisId { get; set; }

    /// <summary>Weighted fit score (matched weight / total weight × 100), 0-100.</summary>
    public int MatchScore { get; set; }

    public KeywordMatch KeywordMatch { get; set; } = new();
    public AtsParseability AtsParseability { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
}

public class KeywordMatch
{
    /// <summary>JD skills found in the resume (case-insensitive, word-boundary).</summary>
    [JsonPropertyName("matched")]
    public List<string> Matched { get; set; } = new();

    /// <summary>JD skills not found in the resume.</summary>
    [JsonPropertyName("missing")]
    public List<string> Missing { get; set; } = new();
}

public class AtsParseability
{
    /// <summary>ATS friendliness score (100 − 15 × missing labeled sections).</summary>
    [JsonPropertyName("score")]
    public int Score { get; set; }

    /// <summary>Detected parseability issues, if any.</summary>
    [JsonPropertyName("issues")]
    public List<string> Issues { get; set; } = new();
}
