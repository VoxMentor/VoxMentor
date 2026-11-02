namespace VoxMentor.Application.Features.Resume.AnalyzeResume;

/// <summary>ATS fit report for a resume against a parsed job description.</summary>
public sealed class AnalyzeResumeResultDto
{
    /// <summary>Weighted match score 0-100 (matched skill weights / total JD weight).</summary>
    public int MatchScore { get; set; }

    /// <summary>Matched vs missing JD skills (by name).</summary>
    public ResumeKeywordMatchDto KeywordMatch { get; set; } = new();

    /// <summary>Heuristic ATS parseability report for the extracted resume text.</summary>
    public ResumeAtsParseabilityDto AtsParseability { get; set; } = new();

    /// <summary>Actionable suggestions derived from the gaps and parseability issues.</summary>
    public List<string> Suggestions { get; set; } = new();
}

public sealed class ResumeKeywordMatchDto
{
    public List<string> Matched { get; set; } = new();
    public List<string> Missing { get; set; } = new();
}

public sealed class ResumeAtsParseabilityDto
{
    public int Score { get; set; }
    public List<string> Issues { get; set; } = new();
}
