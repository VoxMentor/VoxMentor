using Microsoft.AspNetCore.SignalR;

namespace VoxMentor.Api.Hubs;

public class MasteryHub : Hub
{
    public async Task SendMasteryUpdated(string conceptId, double newMastery, double delta)
    {
        await Clients.Caller.SendAsync("MasteryUpdated", new
        {
            ConceptId = conceptId,
            NewMastery = newMastery,
            Delta = delta
        });
    }

    public async Task SendReadinessChanged(double newScore, double delta)
    {
        await Clients.Caller.SendAsync("ReadinessChanged", new
        {
            NewScore = newScore,
            Delta = delta
        });
    }
}
