namespace VoxMentor.Application.Features.KnowledgeGraph.GetEligibleConcepts;

/// <summary>Student-specific eligibility buckets for the knowledge graph.</summary>
public class EligibleConceptsDto
{
    /// <summary>Concepts with all prerequisites met (vacuous if none).</summary>
    public IReadOnlyList<EligibleConceptDto> Eligible { get; set; } = Array.Empty<EligibleConceptDto>();

    /// <summary>Concepts with exactly one unmet prerequisite.</summary>
    public IReadOnlyList<EligibleConceptDto> AlmostEligible { get; set; } = Array.Empty<EligibleConceptDto>();
}

/// <summary>A concept in one of the eligibility buckets.</summary>
public class EligibleConceptDto
{
    /// <summary>Concept id.</summary>
    public Guid ConceptId { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Category the concept belongs to.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Difficulty level 1-5.</summary>
    public int DifficultyLevel { get; set; }
}
