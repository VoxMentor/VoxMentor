namespace VoxMentor.Application.Features.Practice.GetReadiness;

/// <summary>
/// JD-weighted readiness score with per-skill breakdown, gap analysis, and
/// estimated time-to-ready.
/// </summary>
public class ReadinessDto
{
    /// <summary>Weighted readiness score (0–100).</summary>
    public float Score { get; set; }

    /// <summary>Maximum possible score.</summary>
    public int MaxScore { get; set; } = 100;

    /// <summary>Per-skill contribution to the score.</summary>
    public List<SkillBreakdownDto> Breakdown { get; set; } = new();

    /// <summary>Skills where mastery is incomplete, ordered by severity descending.</summary>
    public List<SkillGapDto> Gaps { get; set; } = new();

    /// <summary>Estimated weeks to reach full readiness based on the JD's own estimate.</summary>
    public int EstimatedWeeksToReady { get; set; }
}

/// <summary>One skill's contribution to the readiness score.</summary>
public class SkillBreakdownDto
{
    public string Topic { get; set; } = string.Empty;
    public float Mastery { get; set; }
    public float JdWeight { get; set; }
    public float Contribution { get; set; }
}

/// <summary>A skill gap ranked by severity.</summary>
public class SkillGapDto
{
    public string Topic { get; set; } = string.Empty;
    public float Severity { get; set; }
    public string Recommendation { get; set; } = string.Empty;
}
