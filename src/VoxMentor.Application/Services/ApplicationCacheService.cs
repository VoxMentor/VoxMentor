using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace VoxMentor.Application.Services;

/// <summary>
/// Thin wrapper around <see cref="IDistributedCache"/> with JSON serialization,
/// 300-second TTL, and graceful degradation: cache reads that throw are treated
/// as misses, and cache writes that throw are ignored so a failing Redis does not
/// block the response.
/// </summary>
public static class ApplicationCacheService
{
    /// <summary>
    /// Retrieves a cached value, or null on miss or if the cache read fails.
    /// </summary>
    public static T? GetT<T>(IDistributedCache cache, string key) where T : class
    {
        try
        {
            var cached = cache.GetString(key);
            if (cached is null) return null;
            return JsonSerializer.Deserialize<T>(cached);
        }
        catch
        {
            // Treat cache-read failures as misses so the caller falls through to the DB.
            return null;
        }
    }

    /// <summary>
    /// Stores a value with a 300-second absolute expiration. Write failures are
    /// swallowed so a failing Redis does not block the response.
    /// </summary>
    public static void SetT<T>(IDistributedCache cache, string key, T value) where T : class
    {
        try
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(300)
            };
            var json = JsonSerializer.Serialize(value);
            cache.SetString(key, json, options);
        }
        catch
        {
            // Best-effort: if the cache write fails, continue without caching.
        }
    }
}