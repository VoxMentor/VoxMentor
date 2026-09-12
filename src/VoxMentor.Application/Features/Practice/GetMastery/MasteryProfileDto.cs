namespace VoxMentor.Application.Features.Practice.GetMastery;

/// <summary>
/// The student's mastery profile across every concept in the knowledge graph,
/// plus an overall readiness summary.
/// </summary>
public class MasteryProfileDto
{
    /// <summary>Per-concept mastery rows, ordered by category, difficulty, then name.</summary>
    public List<MasteryConceptDto> Concepts { get; set; } = new();

    /// <summary>Number of concepts with mastery at or above the mastered threshold.</summary>
    public int MasteredCount { get; set; }

    /// <summary>Total number of concepts in the knowledge graph.</summary>
    public int TotalConcepts { get; set; }

    /// <summary>
    /// Average mastery across all concepts on a 0-1 scale, counting unpracticed
    /// concepts as 0. Honest progress indicator: starting nothing yields 0.
    /// </summary>
    public float OverallReadiness { get; set; }
}

/// <summary>One concept's mastery state for the requesting student.</summary>
public class MasteryConceptDto
{
    public Guid ConceptId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public int DifficultyLevel { get; set; }

    /// <summary>BKT mastery probability; null when the student never practiced the concept.</summary>
    public float? MasteryProbability { get; set; }

    public int CorrectAttempts { get; set; }

    public int IncorrectAttempts { get; set; }

    /// <summary>Whether mastery has reached the 0.85 mastered threshold.</summary>
    public bool IsMastered { get; set; }

    /// <summary>Last time the student practiced this concept; null when never practiced.</summary>
    public DateTime? LastPracticedAt { get; set; }
}
