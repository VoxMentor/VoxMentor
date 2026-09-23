using StackExchange.Redis;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.RateLimiting;

/// <summary>
/// Cluster-safe sliding-window limiter: one sorted-set key per partition,
/// prune + check + record done atomically in a single Lua script.
/// ponytail: fail-closed — Redis down surfaces as an error, not a silent bypass.
/// </summary>
public class RedisSlidingWindowRateLimiter : IRateLimiter
{
    private const string Script = """
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        local limit = tonumber(ARGV[3])
        redis.call('ZREMRANGEBYSCORE', key, 0, now - window)
        local n = redis.call('ZCARD', key)
        if n >= limit then
            local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
            local retry = tonumber(oldest[2]) + window - now
            if retry < 0 then retry = 0 end
            return {0, math.ceil(retry / 1000)}
        end
        redis.call('ZADD', key, now, ARGV[4])
        redis.call('PEXPIRE', key, window)
        return {1, 0}
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly TimeProvider _clock;

    public RedisSlidingWindowRateLimiter(
        IConnectionMultiplexer redis,
        int limit = 10,
        TimeSpan? window = null,
        TimeProvider? clock = null)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "limit must be positive.");
        }

        _redis = redis;
        _limit = limit;
        _window = window ?? TimeSpan.FromHours(1);
        _clock = clock ?? TimeProvider.System;
    }

    public async Task CheckAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        var db = _redis.GetDatabase();
        var result = await db.ScriptEvaluateAsync(
            Script,
            keys: new RedisKey[] { key },
            values: new RedisValue[]
            {
                now,
                (long)_window.TotalMilliseconds,
                _limit,
                Guid.NewGuid().ToString("N")
            });

        var allowed = (int)result[0];
        if (allowed == 0)
        {
            throw new RateLimitException((int)result[1]);
        }
    }
}
