using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Registered when Hangfire storage is disabled (integration tests, bare
/// deployments). Fails fast so uploads are rejected instead of returning 202
/// with a Pending job that can never run; the handler's catch deletes the
/// staged file and marks the saved row Failed.
/// </summary>
public sealed class NullTextbookIngestionQueue : ITextbookIngestionQueue
{
    public void Enqueue(Guid jobId, string filePath)
        => throw new InvalidOperationException(
            "Background ingestion is disabled (no Hangfire storage configured).");

    public void Schedule(Guid jobId, string filePath, TimeSpan delay)
        => throw new InvalidOperationException(
            "Background ingestion is disabled (no Hangfire storage configured).");
}
