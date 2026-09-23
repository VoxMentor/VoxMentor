namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Per-user sliding-window rate limiter for expensive endpoints (e.g. tutor ask).
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Records a hit for <paramref name="key"/> and throws when the configured
    /// limit for the current window has been exceeded.
    /// </summary>
    /// <param name="key">Partition key, typically the user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Common.Exceptions.RateLimitException">Limit exceeded.</exception>
    Task CheckAsync(string key, CancellationToken cancellationToken = default);
}
