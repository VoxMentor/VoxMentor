using Microsoft.AspNetCore.SignalR;

namespace VoxMentor.Api.Hubs;

public class InterviewHub : Hub
{
    public async Task StartMock(string jdId, string type, bool voiceMode)
    {
        var interviewId = Guid.NewGuid().ToString();

        // TODO: Initialize mock interview session with AI
        // For now, send placeholder messages
        await Clients.Caller.SendAsync("InterviewerMessage",
            "Hi, I'm your AI interviewer today. Let's begin.");

        await Clients.Caller.SendAsync("InterviewerMessage",
            "Here's your first problem: Given an array, find the maximum sum of a contiguous subarray.");

        await Clients.Caller.SendAsync("InterviewComplete", new
        {
            InterviewId = interviewId,
            Score = 0
        });
    }
}
