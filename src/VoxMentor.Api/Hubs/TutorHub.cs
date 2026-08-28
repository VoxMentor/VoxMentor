using Microsoft.AspNetCore.SignalR;

namespace VoxMentor.Api.Hubs;

public class TutorHub : Hub
{
    public async Task AskTutor(string conceptId, string question)
    {
        var sessionId = Guid.NewGuid().ToString();

        // TODO: Call AI Coach service to generate response
        // For now, send a placeholder token stream
        await Clients.Caller.SendAsync("TutorToken", $"[Tutor response for concept '{conceptId}': ");
        await Clients.Caller.SendAsync("TutorToken", $"'{question}'");
        await Clients.Caller.SendAsync("TutorToken", " - streaming will be implemented with RAG backend.]");

        await Clients.Caller.SendAsync("TutorComplete", new
        {
            SessionId = sessionId,
            TotalChunks = 3
        });
    }
}
