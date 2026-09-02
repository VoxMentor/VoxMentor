namespace VoxMentor.Domain.Entities;

public class BktParameters
{
    private float _priorKnowledge = 0.1f;
    private float _learnRate = 0.3f;
    private float _guessRate = 0.2f;
    private float _slipRate = 0.1f;

    public Guid Id { get; set; }
    public Guid ConceptId { get; set; }

    public float PriorKnowledge
    {
        get => _priorKnowledge;
        set => _priorKnowledge = ClampProbability(value);
    }

    public float LearnRate
    {
        get => _learnRate;
        set => _learnRate = ClampProbability(value);
    }

    public float GuessRate
    {
        get => _guessRate;
        set => _guessRate = ClampProbability(value);
    }

    public float SlipRate
    {
        get => _slipRate;
        set => _slipRate = ClampProbability(value);
    }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    private static float ClampProbability(float value) =>
        float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 1f);
}
