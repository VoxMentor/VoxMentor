namespace VoxMentor.Domain.Entities;

public class JobDescription
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string RawText { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public int EstimatedWeeks { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
