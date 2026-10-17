using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Registered when Hangfire storage is disabled (integration tests, bare
/// deployments). Fails fast so asks are rejected instead of returning 202
/// with a Pending session that can never run; the handler's catch cleans up
/// the saved row (mirrors NullTextbookIngestionQueue).
/// </summary>
public sealed class NullTutorSessionQueue : ITutorSessionQueue
{
    public void Enqueue(Guid sessionId)
        => throw new InvalidOperationException(
            "Background tutor generation is disabled (no Hangfire storage configured).");
}
