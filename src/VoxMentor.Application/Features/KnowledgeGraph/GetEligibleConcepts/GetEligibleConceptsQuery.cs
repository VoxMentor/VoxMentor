using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.KnowledgeGraph.GetEligibleConcepts;

/// <summary>Requests eligible and almost-eligible concepts for the current student.</summary>
public class GetEligibleConceptsQuery : IRequest<ApiResponse<EligibleConceptsDto>>
{
}
