using Microsoft.AspNetCore.SignalR;

namespace VoxMentor.Api.Hubs;

public class MasteryHub : Hub
{
    // Server → Client events (published via IHubContext<MasteryHub> from trusted server code):
    //   MasteryUpdated: { conceptId, newMastery, delta }
    //   ReadinessChanged: { newScore, delta }
    //
    // Example usage from a controller or service:
    //   await _hubContext.Clients.User(userId).SendAsync("MasteryUpdated", new { ... });
}
