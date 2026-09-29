using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Tests;

/// <summary>
/// Shared ICurrentUserService for hand-built ApplicationDbContexts in tests
/// (#57). Files with their own private stub keep it; this serves the rest.
/// </summary>
internal sealed class TestCurrentUser : ICurrentUserService
{
    public string? UserId { get; set; } = "test-user";
}
