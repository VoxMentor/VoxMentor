using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.KnowledgeGraph.GetPrerequisiteChain;

/// <summary>Requests the upward prerequisite chain for one concept.</summary>
public class GetPrerequisiteChainQuery : IRequest<ApiResponse<IReadOnlyList<PrerequisiteChainItemDto>>>
{
    /// <summary>Concept whose prerequisites should be walked.</summary>
    public Guid ConceptId { get; init; }
}
