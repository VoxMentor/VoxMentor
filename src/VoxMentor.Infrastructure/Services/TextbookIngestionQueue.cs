using Hangfire;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Hangfire-backed implementation of the textbook ingestion queue.
/// </summary>
public class TextbookIngestionQueue : ITextbookIngestionQueue
{
    private readonly IBackgroundJobClient _client;

    public TextbookIngestionQueue(IBackgroundJobClient client)
    {
        _client = client;
    }

    public void Enqueue(Guid jobId, string filePath)
        => _client.Enqueue<IngestionJob>(job => job.Execute(jobId, filePath, CancellationToken.None));
}
