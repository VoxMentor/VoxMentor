using VoxMentor.Application.Features.Practice.GetReadiness;

namespace VoxMentor.Application.Features.Jd.GetJd;

/// <summary>
/// Parsed JD plus the current student's readiness, gaps, and week-by-week
/// roadmap, returned by GET /api/v1/jd/{jdId} (#118).
/// </summary>
public class GetJdResultDto
{
    public Guid JdId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>Technical topic → weight (subset of the JD's skill weights).</summary>
    public Dictionary<string, float> TechnicalSkills { get; set; } = new(StringComparer.Ordinal);

    /// <summary>HR/behavioral topic → weight.</summary>
    public Dictionary<string, float> HrSkills { get; set; } = new(StringComparer.Ordinal);

    public string Difficulty { get; set; } = string.Empty;

    /// <summary>Weighted readiness score (0–100), from the shared ReadinessCalculator.</summary>
    public float ReadinessScore { get; set; }

    /// <summary>Skills where mastery is incomplete, ordered by severity descending.</summary>
    public List<SkillGapDto> Gaps { get; set; } = new();

    public int EstimatedWeeks { get; set; }

    /// <summary>Concepts bucketed by gap severity across the estimated weeks.</summary>
    public List<RoadmapWeekDto> Roadmap { get; set; } = new();

    public DateTime CreatedAt { get; set; }
}

/// <summary>One week of the study roadmap.</summary>
public class RoadmapWeekDto
{
    /// <summary>1-based week number.</summary>
    public int Week { get; set; }

    /// <summary>Comma-joined focus topics for the week (highest severity first).</summary>
    public string Focus { get; set; } = string.Empty;

    /// <summary>Concepts matching the week's focus topics.</summary>
    public List<Guid> ConceptIds { get; set; } = new();
}
