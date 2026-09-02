namespace VoxMentor.Domain.Entities;

public class Question
{
    public Guid Id { get; set; }
    public Guid ConceptId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Difficulty { get; set; }
    public string[] TestCases { get; set; } = Array.Empty<string>();
    public string[] ExampleInputs { get; set; } = Array.Empty<string>();
    public string[] ExampleOutputs { get; set; } = Array.Empty<string>();
    public string[] StarterCode { get; set; } = Array.Empty<string>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
