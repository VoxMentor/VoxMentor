namespace VoxMentor.Application.Features.Jd.UploadJd;

/// <summary>Parsed JD summary returned by POST /api/v1/jd/upload (#117).</summary>
public class UploadJdResultDto
{
    public Guid JdId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>Topic → weight, normalized to sum to 1.0 (empty when none found).</summary>
    public Dictionary<string, float> TechnicalSkills { get; set; } = new(StringComparer.Ordinal);

    /// <summary>HR/behavioral topic → weight, normalized to sum to 1.0.</summary>
    public Dictionary<string, float> HrSkills { get; set; } = new(StringComparer.Ordinal);

    public string Difficulty { get; set; } = string.Empty;
    public int EstimatedWeeks { get; set; }
    public DateTime CreatedAt { get; set; }
}
