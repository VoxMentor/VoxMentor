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
        if (cache is null) return;
        try { await cache.RemoveAsync(key, cancellationToken); } catch (Exception ex) { OnFailure?.Invoke(ex); }
    }

    /// <summary>Gets the per-user, per-scope cache version. Returns 0 when unset or on failure.</summary>
    public static int GetVersion(IDistributedCache? cache, string scope, string userId)
    {
        if (cache is null) return 0;
        try
        {
            var cached = cache.GetString($"cache_version:{scope}:{userId}");
            if (cached is null) return 0;
            return int.TryParse(cached, out var version) ? version : 0;
        }
        catch (Exception ex)
        {
            // Treat any failure as version 0 — degrades to unversioned keys.
            OnFailure?.Invoke(ex);
            return 0;
        }
    }

    /// <summary>Gets the per-user, per-scope cache version. Returns 0 when unset or on failure.</summary>
    public static async Task<int> GetVersionAsync(IDistributedCache? cache, string scope, string userId, CancellationToken cancellationToken = default)
    {
        if (cache is null) return 0;
        try
        {
            var cached = await cache.GetStringAsync($"cache_version:{scope}:{userId}", cancellationToken);
            if (cached is null) return 0;
            return int.TryParse(cached, out var version) ? version : 0;
        }
        catch (Exception ex)
        {
            // Treat any failure as version 0 — degrades to unversioned keys.
            OnFailure?.Invoke(ex);
            return 0;
        }
    }

    /// <summary>Builds a per-user, per-scope versioned cache key so an in-flight
    /// read from before a mastery update cannot repopulate the current key after
    /// the version has been bumped. Writes land under the version current at
    /// write time; invalidation bumps the version, orphaning all old-version
    /// keys for that scope only — other scopes keep their cache hits.</summary>
    public static string VersionedKey(IDistributedCache? cache, string userId, string scope, string key)
    {
        ValidateCacheKeyInput(userId, scope);
        var version = GetVersion(cache, scope, userId);
        return $"{key}:{userId}:{scope}:{version}";
    }

    /// <summary>Builds a per-user, per-scope versioned cache key so an in-flight
    /// read from before a mastery update cannot repopulate the current key after
    /// the version has been bumped. Writes land under the version current at
    /// write time; invalidation bumps the version, orphaning all old-version
    /// keys for that scope only — other scopes keep their cache hits.</summary>
    public static async Task<string> VersionedKeyAsync(IDistributedCache? cache, string userId, string scope, string key, CancellationToken cancellationToken = default)
    {
        ValidateCacheKeyInput(userId, scope);
        var version = await GetVersionAsync(cache, scope, userId, cancellationToken);
        return $"{key}:{userId}:{scope}:{version}";
    }

    /// <summary>Validates userId and scope before embedding them in cache keys,
    /// preventing key injection via unexpected characters.</summary>
    private static void ValidateCacheKeyInput(string userId, string scope)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Contains(':') || scope.Contains(':'))
            throw new ArgumentException("Cache key input contains disallowed characters.", userId.Contains(':') ? nameof(userId) : nameof(scope));
    }

    /// <summary>Invalidates all versioned cache entries for a user within the
    /// given scope by removing the keys at the current version and bumping the
    /// version. Other scopes are untouched, so unrelated cache entries keep
    /// their hits.</summary>
    public static void Invalidate(IDistributedCache? cache, string userId, string scope, params string[] keys)
    {
        if (cache is null) return;
        ValidateCacheKeyInput(userId, scope);
        try
        {
            int version = 0;
            bool readOk = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var cached = cache.GetString($"cache_version:{scope}:{userId}");
                    version = cached is null ? 0 : (int.TryParse(cached, out var v) ? v : 0);
                    readOk = true;
                    break;
                }
                catch (Exception ex)
                {
                    OnFailure?.Invoke(ex);
                    if (attempt == 2) throw;
                }
            }
            if (!readOk) return;

            foreach (var key in keys)
            {
                cache.Remove($"{key}:{userId}:{scope}:{version}");
            }
            cache.SetString($"cache_version:{scope}:{userId}", (version + 1).ToString());
        }
        catch (Exception ex)
        {
            OnFailure?.Invoke(ex);
        }
    }

    /// <summary>Invalidates all versioned cache entries for a user within the
    /// given scope by removing the keys at the current version and bumping the
    /// version. Other scopes are untouched, so unrelated cache entries keep
    /// their hits.</summary>
    public static async Task InvalidateAsync(IDistributedCache? cache, string userId, string scope, CancellationToken cancellationToken = default, params string[] keys)
    {
        if (cache is null) return;
        ValidateCacheKeyInput(userId, scope);
        try
        {
            int version = 0;
            bool readOk = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var cached = await cache.GetStringAsync($"cache_version:{scope}:{userId}", cancellationToken);
                    version = cached is null ? 0 : (int.TryParse(cached, out var v) ? v : 0);
                    readOk = true;
                    break;
                }
                catch (Exception ex)
                {
                    OnFailure?.Invoke(ex);
                    if (attempt == 2) throw;
                }
            }
            if (!readOk) return;

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
