using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace VoxMentor.Application.Services;

/// <summary>
/// Thin, failure-tolerant wrapper over <see cref="IDistributedCache"/> for the
/// cache-through read handlers. Read failures are treated as cache misses and
/// fall through to the database; write failures are tolerated so the database
/// result is still returned.
/// </summary>
public static class ApplicationCacheService
{
    private const int CacheTtlSeconds = 300;

    /// <summary>Optional observability hook for cache failures. Configure at startup (e.g. in DI) to log/metric.</summary>
    public static Action<Exception>? OnFailure { get; set; }

    /// <summary>Cache-through read: returns <c>null</c> on a miss or any read failure.</summary>
    public static T? GetT<T>(IDistributedCache? cache, string key) where T : class
    {
        if (cache is null) return null;
        try
        {
            var cached = cache.GetString(key);
            return cached is null ? null : JsonSerializer.Deserialize<T>(cached);
        }
        catch (Exception ex)
        {
            // Treat any cache read failure as a miss — fall through to the DB.
            OnFailure?.Invoke(ex);
            return null;
        }
    }

    /// <summary>Cache-through write: tolerates failures without blocking the caller.</summary>
    public static void SetT<T>(IDistributedCache? cache, string key, T value) where T : class
    {
        if (cache is null) return;
        try
        {
            cache.SetString(key, JsonSerializer.Serialize(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheTtlSeconds) });
        }
        catch (Exception ex)
        {
            // Tolerate cache write failures — the DB result is still returned.
            OnFailure?.Invoke(ex);
        }
    }

    /// <summary>Invalidates a cache entry (tolerates failures).</summary>
    public static void Remove(IDistributedCache? cache, string key)
    {
        if (cache is null) return;
        try { cache.Remove(key); } catch (Exception ex) { OnFailure?.Invoke(ex); }
    }
}
