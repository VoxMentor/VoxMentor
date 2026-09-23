namespace VoxMentor.Application.Features.KnowledgeGraph.GetPrerequisiteChain;

/// <summary>One node in the upward prerequisite chain.</summary>
public class PrerequisiteChainItemDto
{
    /// <summary>Concept id of this prerequisite.</summary>
    public Guid ConceptId { get; set; }

    /// <summary>Display name of the prerequisite concept.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Distance from the requested concept (1 = direct prerequisite).</summary>
    public int Depth { get; set; }
}
