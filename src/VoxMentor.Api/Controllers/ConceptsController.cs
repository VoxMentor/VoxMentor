using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.KnowledgeGraph.GetPrerequisiteChain;

namespace VoxMentor.Api.Controllers;

/// <summary>
/// Knowledge-graph lookup endpoints for concepts and their prerequisites.
/// </summary>
[ApiController]
[Route("api/v1/concepts")]
public class ConceptsController : ControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initializes the controller with the MediatR sender.</summary>
    public ConceptsController(ISender sender) => _sender = sender;

    /// <summary>
    /// Returns the upward prerequisite chain for a concept (transitive closure,
    /// ordered by depth). Empty list if the concept has no prerequisites.
    /// </summary>
    [HttpGet("{id:guid}/prerequisites")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PrerequisiteChainItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPrerequisites(Guid id, CancellationToken cancellationToken)
    {
        var response = await _sender.Send(new GetPrerequisiteChainQuery { ConceptId = id }, cancellationToken);
        return Ok(response);
    }
}
