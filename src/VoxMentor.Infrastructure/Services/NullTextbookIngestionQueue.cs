using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Used when Hangfire storage is disabled (integration tests, design-time DI).
/// Enqueued jobs are dropped — status stays Pending.
/// </summary>
public sealed class NullTextbookIngestionQueue : ITextbookIngestionQueue
{
    public void Enqueue(Guid jobId, string filePath)
    {
        // ponytail: intentional no-op; only registered when ConnectionStrings:Hangfire is empty
    }
}
