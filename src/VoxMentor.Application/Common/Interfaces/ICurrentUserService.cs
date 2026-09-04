namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Provides the authenticated user's identity to Application-layer handlers
/// without coupling them to ASP.NET Core.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// The current user's identifier from the JWT <c>sub</c> or
    /// <c>NameIdentifier</c> claim, or <c>null</c> when unauthenticated.
    /// </summary>
    string? UserId { get; }
}
