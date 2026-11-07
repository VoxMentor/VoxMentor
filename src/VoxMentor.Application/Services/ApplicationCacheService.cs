using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace VoxMentor.Application.Services;

public class ApplicationCacheService
{
    private readonly IDistributedCache _cache;
    private const int CacheTTLSeconds = 300;

    public ApplicationCacheService(IDistributedCache cache)
    {
        _cache = cache;
    }

    // Cache-through pattern: try cache first, if miss then read from DB
    // Returns null if Redis is unavailable (fall through to DB)
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

    public static void SetT<T>(IDistributedCache cache, string key, T value) where T : class
    {
        try
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheTTLSeconds)
            };
            var json = JsonSerializer.Serialize(value);
            cache.SetString(key, json, options);
        }
        catch
        {
            // Best-effort: if the cache write fails, continue without caching.
        }
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
    }
}