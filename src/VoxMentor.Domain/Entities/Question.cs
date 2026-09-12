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

    /// <summary>Number of trailing test cases hidden from students (aggregate counts still shown).</summary>
    public int HiddenTestCaseCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
