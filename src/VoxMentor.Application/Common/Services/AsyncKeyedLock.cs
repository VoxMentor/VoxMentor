using System.Collections.Concurrent;

namespace VoxMentor.Application.Common.Services;

/// <summary>
/// Per-key async mutex serializing one shared write per key: the mock-interview
/// daily quota (keyed by student) and transcript appends (keyed by interview) —
/// #124. Non-reentrant; the returned handle releases on dispose.
/// ponytail: in-process only — correct for the single-API-instance deployment.
/// Multi-instance upgrade path: a DB unique index on (UserId, day) for the
/// quota and an optimistic concurrency token on TranscriptJson for answers.
/// Keys are never evicted, so memory grows with distinct keys per process
/// (bounded by daily students + interviews); add eviction if that ever matters.
/// </summary>
public sealed class AsyncKeyedLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>Waits for exclusive ownership of <paramref name="key"/>; dispose to release.</summary>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _released;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _semaphore.Release();
            }
        }
    }
}
