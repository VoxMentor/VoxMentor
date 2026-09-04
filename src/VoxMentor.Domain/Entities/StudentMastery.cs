namespace VoxMentor.Domain.Entities;

public class StudentMastery
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid ConceptId { get; set; }
    public float MasteryProbability { get; set; } = 0.1f;
    public int CorrectAttempts { get; set; }
    public int IncorrectAttempts { get; set; }
    public DateTime? LastPracticedAt { get; set; }
    // Optimistic concurrency token mapped to PostgreSQL's xmin system column
    // (Npgsql maps uint + IsRowVersion to xmin; no table column required).
    public uint RowVersion { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
