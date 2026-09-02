namespace VoxMentor.Domain.Entities;

public class Prerequisite
{
    public Guid Id { get; set; }
    public Guid ConceptId { get; set; }
    public Guid RequiredConceptId { get; set; }
    public int Weight { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
