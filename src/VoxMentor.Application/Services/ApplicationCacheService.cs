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

    /// <summary>Gets the per-user cache version. Returns 0 when unset or on failure.</summary>
    public static int GetVersion(IDistributedCache? cache, string userId)
    {
        if (cache is null) return 0;
        try
        {
            var cached = cache.GetString($"cache_version:{userId}");
            return cached is null ? 0 : int.Parse(cached);
        }
        catch
        {
            // Treat any failure as version 0 — degrades to unversioned keys.
            return 0;
        }
    }

    /// <summary>Builds a per-user versioned cache key so an in-flight read from
    /// before a mastery update cannot repopulate the current key after the
    //  version has been bumped. Writes land under the version current at write
    //  time; invalidation bumps the version, orphaning all old-version keys.</summary>
    public static string VersionedKey(IDistributedCache? cache, string userId, string key)
    {
        var version = GetVersion(cache, userId);
        return $"{key}:{userId}:{version}";
    }

    /// <summary>Invalidates all versioned cache entries for a user by removing
    //  the keys at the current version and bumping the version. Stale writes
    //  from in-flight reads land under the old version and are never read again.</summary>
    public static void Invalidate(IDistributedCache? cache, string userId, params string[] keys)
    {
        if (cache is null) return;
        try
        {
            var version = GetVersion(cache, userId);
            foreach (var key in keys)
            {
                cache.Remove($"{key}:{userId}:{version}");
            }
            cache.SetString($"cache_version:{userId}", (version + 1).ToString());
        }
        catch (Exception ex)
        {
            OnFailure?.Invoke(ex);
        }
    }
}
