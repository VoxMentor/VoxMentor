namespace VoxMentor.Application.Common.Models;

/// <summary>
/// Structured job-description parse: either Ollama's output (#117) or the
/// keyword-heuristic fallback. Skill weights are raw relative scores — they
/// are NOT trusted to sum to 1.0; <see cref="VoxMentor.Application.Services.JdWeightNormalizer"/>
/// owns that.
/// </summary>
public class JdParseResult
{
    public string CompanyName { get; set; } = "Unknown";
    public string Role { get; set; } = "Unknown";
    public Dictionary<string, float> TechnicalSkills { get; set; } = new();
    public Dictionary<string, float> HrSkills { get; set; } = new();
    public string Difficulty { get; set; } = "Medium";
    public int EstimatedWeeks { get; set; } = 6;
}
