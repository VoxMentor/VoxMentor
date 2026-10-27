namespace VoxMentor.Application.Common.Exceptions;

public class ValidationException : Exception
{
    /// <summary>The validation errors carried by this exception, keyed by field name.</summary>
    public IDictionary<string, string[]> Errors { get; }

    /// <summary>Creates a validation failure carrying the given field errors.</summary>
    public ValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation failures have occurred.")
    {
        Errors = errors;
    }
}

public class ConflictException : Exception
{
    /// <summary>Creates a conflict failure with the given message.</summary>
    public ConflictException(string message) : base(message)
    {
    }
}

public class NotFoundException : Exception
{
    /// <summary>Creates a not-found failure with the given message.</summary>
    public NotFoundException(string message) : base(message)
    {
    }
}

public class RateLimitException : Exception
{
    /// <summary>Seconds until the client may retry.</summary>
    public int RetryAfterSeconds { get; }

    /// <summary>Creates a rate-limit failure, clamping the retry-after seconds to at least 1.</summary>
    public RateLimitException(int retryAfterSeconds)
        : base("Rate limit exceeded. Please retry later.")
    {
        RetryAfterSeconds = Math.Max(1, retryAfterSeconds);
    }
}

public class ServiceUnavailableException : Exception
{
    /// <summary>Creates a service-unavailable failure with the given message.</summary>
    public ServiceUnavailableException(string message)
        : base(message)
    {
    }
}

public class MockInterviewGenerationException : Exception
{
    /// <summary>
    /// The AI interviewer's dialogue generation failed (Ollama unreachable,
    /// timeout, mid-stream error, empty output — #126). The hub surfaces this
    /// as InterviewError; the REST handlers catch it and degrade to a canned line.
    /// </summary>
    public MockInterviewGenerationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
