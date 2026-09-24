using StackExchange.Redis;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.RateLimiting;

/// <summary>
/// Cluster-safe sliding-window limiter: one sorted-set key per partition,
/// prune + check + record done atomically in a single Lua script using Redis TIME
/// so every API instance shares one clock.
/// ponytail: fail-closed — Redis down surfaces as an error, not a silent bypass.
/// </summary>
public class RedisSlidingWindowRateLimiter : IRateLimiter
{
    private const string Script = """
        local time = redis.call('TIME')
        local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)
        local key = KEYS[1]
        local window = tonumber(ARGV[1])
        local limit = tonumber(ARGV[2])
        redis.call('ZREMRANGEBYSCORE', key, 0, now - window)
        local n = redis.call('ZCARD', key)
        if n >= limit then
            local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
            local retry = tonumber(oldest[2]) + window - now
            if retry < 0 then retry = 0 end
            return {0, math.ceil(retry / 1000)}
        end
        redis.call('ZADD', key, now, ARGV[3])
        redis.call('PEXPIRE', key, window)
        return {1, 0}
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly int _limit;
    private readonly TimeSpan _window;

    public RedisSlidingWindowRateLimiter(
        IConnectionMultiplexer redis,
        int limit = 10,
        TimeSpan? window = null)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "limit must be positive.");
        }

        _redis = redis;
        _limit = limit;
        _window = window ?? TimeSpan.FromHours(1);
    }

    public async Task CheckAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var result = await db.ScriptEvaluateAsync(
            Script,
            keys: new RedisKey[] { key },
            values: new RedisValue[]
            {
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
