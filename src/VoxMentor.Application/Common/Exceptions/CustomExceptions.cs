namespace VoxMentor.Application.Common.Exceptions;

public class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation failures have occurred.")
    {
        Errors = errors;
    }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}

public class RateLimitException : Exception
{
    /// <summary>Seconds until the client may retry.</summary>
    public int RetryAfterSeconds { get; }

    public RateLimitException(int retryAfterSeconds)
        : base("Rate limit exceeded. Please retry later.")
    {
        RetryAfterSeconds = Math.Max(1, retryAfterSeconds);
    }
}
