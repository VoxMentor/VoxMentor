using System.Collections.Concurrent;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.RateLimiting;

/// <summary>
/// Sliding-window rate limiter kept in process memory.
/// ponytail: single-instance only; swap for Redis (StackExchange) when multi-instance matters.
/// </summary>
public class InMemorySlidingWindowRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _windows = new();
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly TimeProvider _clock;

    public InMemorySlidingWindowRateLimiter(int limit = 10, TimeSpan? window = null, TimeProvider? clock = null)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "limit must be positive.");
        }

        _limit = limit;
        _window = window ?? TimeSpan.FromHours(1);
        _clock = clock ?? TimeProvider.System;
    }

    public Task CheckAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _clock.GetUtcNow().UtcDateTime;
        PurgeExpired(now);

        var window = _windows.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (window)
        {
            while (window.Count > 0 && now - window.Peek() >= _window)
            {
                window.Dequeue();
            }

            if (window.Count >= _limit)
            {
                var retry = (int)Math.Ceiling((window.Peek() + _window - now).TotalSeconds);
                throw new RateLimitException(retry);
            }

            window.Enqueue(now);
        }

        return Task.CompletedTask;
    }

    private void PurgeExpired(DateTime now)
    {
        // ponytail: O(keys) sweep per call; timer/background purge only if key count ever matters
        foreach (var kv in _windows)
        {
            var q = kv.Value;
            lock (q)
            {
                var pruned = false;
                while (q.Count > 0 && now - q.Peek() >= _window)
                {
                    q.Dequeue();
                    pruned = true;
                }

                // only drop fully-expired keys; fresh in-flight queues (pruned == false) stay
                if (pruned && q.Count == 0)
                {
                    _windows.TryRemove(kv);
                }
            }
        }
    }
}
