namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Provides the current user's raw access token so downstream handlers can
/// forward it to internal microservices that require authentication.
/// </summary>
public interface ICurrentTokenService
{
    /// <summary>The serialized access token for the authenticated user, or null.</summary>
    string? AccessToken { get; }
}
