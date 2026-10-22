using Hangfire;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Hangfire-backed implementation of the tutor session queue.
/// </summary>
public class TutorSessionQueue : ITutorSessionQueue
{
    private readonly IBackgroundJobClient _client;

    /// <summary>Initializes the queue with the Hangfire background job client.</summary>
    public TutorSessionQueue(IBackgroundJobClient client)
    {
        _client = client;
    }

    /// <summary>Enqueues the session's answer generation as a Hangfire background job.</summary>
    public void Enqueue(Guid sessionId)
        => _client.Enqueue<TutorSessionJob>(job => job.Execute(sessionId, CancellationToken.None));
}
