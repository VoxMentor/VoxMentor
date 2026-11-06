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
        var cached = cache.GetString(key);
        if (cached is not null)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(cached);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public static void SetT<T>(IDistributedCache cache, string key, T value) where T : class
    {
        cache.SetString(key, JsonSerializer.Serialize(value), new DistributedCacheEntryOptions{ AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheTTLSeconds) });
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
    }
}