namespace VoxMentor.Domain.Entities;

public class BktParameters
{
    public Guid Id { get; set; }
    public Guid ConceptId { get; set; }
    public float PriorKnowledge { get; set; } = 0.1f;  // P(L₀)
    public float LearnRate { get; set; } = 0.3f;       // P(learn | not learned, correct) — bounded [0,1]
    public float GuessRate { get; set; } = 0.2f;       // P(correct | not learned)
    public float SlipRate { get; set; } = 0.1f;        // P(incorrect | learned)
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
