using Hangfire;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Hangfire-backed implementation of the tutor session queue.
/// </summary>
public class TutorSessionQueue : ITutorSessionQueue
{
    private readonly IBackgroundJobClient _client;

    public TutorSessionQueue(IBackgroundJobClient client)
    {
        _client = client;
    }

    public void Enqueue(Guid sessionId)
        => _client.Enqueue<TutorSessionJob>(job => job.Execute(sessionId, CancellationToken.None));
}
