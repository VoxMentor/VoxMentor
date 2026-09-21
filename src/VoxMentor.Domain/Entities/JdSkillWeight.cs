namespace VoxMentor.Domain.Entities;

public class JdSkillWeight
{
    public Guid Id { get; set; }
    public Guid JobDescriptionId { get; set; }
    public string SkillName { get; set; } = string.Empty;
    public float Weight { get; set; }
    public bool IsTechnical { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
