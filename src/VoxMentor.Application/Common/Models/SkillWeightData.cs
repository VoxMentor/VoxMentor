namespace VoxMentor.Application.Common.Models;

/// <summary>DTO for a JD skill weight, cached independently of the EF entity.</summary>
public sealed record SkillWeightData(string SkillName, float Weight, bool IsTechnical);
