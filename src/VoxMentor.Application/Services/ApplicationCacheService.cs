using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace VoxMentor.Application.Services;

/// <summary>
/// Thin, failure-tolerant wrapper over <see cref="IDistributedCache"/> for the
/// cache-through read handlers. Read failures are treated as cache misses and
/// fall through to the database; write failures are tolerated so the database
/// result is still returned.
/// </summary>
public class ApplicationCacheService
{
    private const int CacheTTLSeconds = 300;

    /// <summary>Optional observability hook for cache failures. Configure at startup (e.g. in DI) to log/metric.</summary>
    public static Action<Exception>? OnFailure
    {
        get => _onFailure;
        set => Interlocked.Exchange(ref _onFailure, value);
    }

    private static Action<Exception>? _onFailure;

    private readonly IDistributedCache? _cache;

    public ApplicationCacheService(IDistributedCache? cache)
    {
        _cache = cache;
    }

    /// <summary>Cache-through read: returns <c>null</c> on a miss or any read failure.</summary>
    public static T? GetT<T>(IDistributedCache? cache, string key) where T : class
    {
        if (cache is null) return null;
        try
        {
            var cached = cache.GetString(key);
            if (cached is null) return null;
            return JsonSerializer.Deserialize<T>(cached);
        }
        catch (Exception ex)
        {
            OnFailure?.Invoke(ex);
            // Treat cache-read failures as misses so the caller falls through to the DB.
            return null;
        }
    }

    /// <summary>Async cache-through read: returns <c>null</c> on a miss or any read failure.</summary>
    public static async Task<T?> GetTAsync<T>(IDistributedCache? cache, string key, CancellationToken cancellationToken = default) where T : class
    {
        if (cache is null) return null;
        try
        {
            var cached = await cache.GetStringAsync(key, cancellationToken);
            if (cached is null) return null;
            return JsonSerializer.Deserialize<T>(cached);
        }
        catch (Exception ex)
        {
            OnFailure?.Invoke(ex);
            // Treat cache-read failures as misses so the caller falls through to the DB.
            return null;
        }
    }

    /// <summary>Cache-through write: tolerates failures without blocking the caller.</summary>
    public static void SetT<T>(IDistributedCache? cache, string key, T value) where T : class
    {
        if (cache is null) return;
        try
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheTTLSeconds)
            };
            var json = JsonSerializer.Serialize(value);
            cache.SetString(key, json, options);
        }
        catch (Exception ex)
        {
            // Tolerate cache write failures — the DB result is still returned.
            OnFailure?.Invoke(ex);
        }
    }

    /// <summary>Async cache-through write: tolerates failures without blocking the caller.</summary>
    public static async Task SetTAsync<T>(IDistributedCache? cache, string key, T value, CancellationToken cancellationToken = default) where T : class
    {
        if (cache is null) return;
        try
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheTTLSeconds)
            };
            var json = JsonSerializer.Serialize(value);
            await cache.SetStringAsync(key, json, options, cancellationToken);
        }
        catch (Exception ex)
        {
            // Tolerate cache write failures — the DB result is still returned.
            OnFailure?.Invoke(ex);
        }
    }

    /// <summary>Async invalidates a cache entry (tolerates failures).</summary>
    public static async Task RemoveAsync(IDistributedCache? cache, string key, CancellationToken cancellationToken = default)
    {
        if (cache is null) return;
        try { await cache.RemoveAsync(key, cancellationToken); } catch (Exception ex) { OnFailure?.Invoke(ex); }
    }

    /// <summary>
    /// Gets the current version for a user/scope, creating it if missing.
    /// Version is stored at "cache_version:{scope}:{userId}".
    /// </summary>
    public static async Task<int> GetVersionAsync(IDistributedCache? cache, string userId, string scope, CancellationToken cancellationToken = default)
    {
        if (cache is null) return 0;
        try
        {
            var versionKey = $"cache_version:{scope}:{userId}";
            var versionStr = await cache.GetStringAsync(versionKey, cancellationToken);
            if (int.TryParse(versionStr, out var version))
                return version;
            return 0;
        }
        catch (Exception ex)
        {
            OnFailure?.Invoke(ex);
            return 0;
        }
    }

    /// <summary>
    /// Builds a versioned cache key: "{key}:{userId}:{scope}:{version}".
    /// </summary>
    public static async Task<string> VersionedKeyAsync(IDistributedCache? cache, string userId, string scope, string key, CancellationToken cancellationToken = default)
    {
        var version = await GetVersionAsync(cache, userId, scope, cancellationToken);
        return $"{key}:{userId}:{scope}:{version}";
    }

    /// <summary>
    /// Invalidates all versioned cache entries for a user within the given scope
    /// by removing the named keys at the current version and bumping the version.
    /// Old keys are never served because their version prefix no longer matches
    /// the current version.
    /// </summary>
    public static async Task InvalidateAsync(IDistributedCache? cache, string userId, string scope, CancellationToken cancellationToken = default, params string[] keys)
    {
        if (cache is null) return;
        try
        {
            // Remove the named keys at the current version so stale reads
            // can't serve them before the version bump takes effect.
            var currentVersion = await GetVersionAsync(cache, userId, scope, cancellationToken);
            foreach (var key in keys)
            {
                await RemoveAsync(cache, $"{key}:{userId}:{scope}:{currentVersion}", cancellationToken);
            }

            // Bump the version so future reads build keys under the new version.
            var newVersion = currentVersion + 1;
            await cache.SetStringAsync($"cache_version:{scope}:{userId}", newVersion.ToString(), cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort: if version bump fails, old keys might still be served
            // but only until TTL expires (300s).
            OnFailure?.Invoke(ex);
        }
    }
}
