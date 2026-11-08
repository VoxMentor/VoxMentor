using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Text.Json;
using System.Threading;

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
    public static Action<Exception>? OnFailure
    {
        get => _onFailure;
        set => Interlocked.Exchange(ref _onFailure, value);
    }

    private static Action<Exception>? _onFailure;

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

    /// <summary>Cache-through read: returns <c>null</c> on a miss or any read failure.</summary>
    public static async Task<T?> GetTAsync<T>(IDistributedCache? cache, string key, CancellationToken cancellationToken = default) where T : class
    {
        if (cache is null) return null;
        try
        {
            var cached = await cache.GetStringAsync(key, cancellationToken);
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

    /// <summary>Cache-through write: tolerates failures without blocking the caller.</summary>
    public static async Task SetTAsync<T>(IDistributedCache? cache, string key, T value, CancellationToken cancellationToken = default) where T : class
    {
        if (cache is null) return;
        try
        {
            await cache.SetStringAsync(key, JsonSerializer.Serialize(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheTtlSeconds) }, cancellationToken);
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

    /// <summary>Invalidates a cache entry (tolerates failures).</summary>
    public static async Task RemoveAsync(IDistributedCache? cache, string key, CancellationToken cancellationToken = default)
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

    /// <summary>Invalidates all versioned cache entries for a user within the
    /// given scope by removing the keys at the current version and bumping the
    /// version. Other scopes are untouched, so unrelated cache entries keep
    /// their hits.</summary>
    public static async Task InvalidateAsync(IDistributedCache? cache, string userId, string scope, CancellationToken cancellationToken = default, params string[] keys)
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

            foreach (var key in keys)
            {
                await cache.RemoveAsync($"{key}:{userId}:{scope}:{version}", cancellationToken);
            }
            await cache.SetStringAsync($"cache_version:{scope}:{userId}", (version + 1).ToString(), cancellationToken);
        }
        catch (Exception ex)
        {
            OnFailure?.Invoke(ex);
        }
    }
}
