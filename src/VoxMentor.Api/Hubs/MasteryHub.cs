using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace VoxMentor.Api.Hubs;

/// <summary>
/// Pushes BKT mastery changes to the owning student's connections.
/// Server → Client events (published via IHubContext&lt;MasteryHub&gt; from
/// <see cref="Services.SignalRMasteryEventPublisher"/>):
///   MasteryUpdated: { conceptId, conceptName, newMastery, delta }
///   ReadinessChanged: { newScore, delta }   (not yet wired)
/// </summary>
[Authorize]
public class MasteryHub : Hub
{
}
