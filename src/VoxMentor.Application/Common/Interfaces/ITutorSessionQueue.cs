namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Hands a tutor session to the background runner. Implemented in
/// Infrastructure with Hangfire; Application stays free of job-framework
/// references (mirrors ITextbookIngestionQueue, #72).
/// </summary>
public interface ITutorSessionQueue
{
    /// <summary>Queues answer generation for an already-persisted Pending session.</summary>
    void Enqueue(Guid sessionId);
}
