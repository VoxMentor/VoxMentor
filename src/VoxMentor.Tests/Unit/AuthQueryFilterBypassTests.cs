using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Auth.Logout;
using VoxMentor.Application.Features.Auth.RefreshToken;
using VoxMentor.Domain.Entities;
using VoxMentor.Infrastructure.Persistence;
using Xunit;
using RefreshTokenEntity = VoxMentor.Domain.Entities.RefreshToken;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Auth token lookups go by secret hash and often run unauthenticated (expired
/// access cookie). They must bypass the #57 user filter (IgnoreQueryFilters)
/// or refresh/logout silently no-op for exactly the sessions that need them.
/// </summary>
public class AuthQueryFilterBypassTests
{
    private sealed class NullCurrentUser : ICurrentUserService
    {
        public string? UserId => null;
    }

    private sealed class IdentityHasher : IRefreshTokenHasher
    {
        public string Hash(string token) => token;
    }

    private sealed class FakeJwt : IJwtTokenGenerator
    {
        public (string Token, DateTimeOffset Expiration) GenerateAccessToken(ApplicationUser user, IList<string> roles)
            => ("access", DateTimeOffset.UtcNow.AddMinutes(5));

        public (string Token, DateTimeOffset Expiration) GenerateRefreshToken()
            => ("new-refresh", DateTimeOffset.UtcNow.AddDays(7));
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options,
        new NullCurrentUser());

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
        => new(
            new UserStore<ApplicationUser>(db),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Enumerable.Empty<IUserValidator<ApplicationUser>>(),
            Enumerable.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);

    [Fact]
    public async Task Refresh_UnauthenticatedContext_FindsAndRotatesToken()
    {
        await using var db = CreateDb();
        db.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            UserName = "u@example.com",
            Email = "u@example.com",
            FullName = "U"
        });
        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            TokenHash = "raw-1",
            ExpiryTime = DateTimeOffset.UtcNow.AddDays(1)
        });
        db.SaveChanges();

        var handler = new RefreshTokenCommandHandler(db, CreateUserManager(db), new FakeJwt(), new IdentityHasher());

        var result = await handler.Handle(new RefreshTokenCommand("raw-1"), CancellationToken.None);

        Assert.True(result.Success);
        var original = db.RefreshTokens.IgnoreQueryFilters().Single(r => r.TokenHash == "raw-1");
        Assert.True(original.IsRevoked, "presented token should be rotated (revoked)");
    }

    [Fact]
    public async Task Logout_UnauthenticatedContext_RevokesToken()
    {
        await using var db = CreateDb();
        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            TokenHash = "raw-2",
            ExpiryTime = DateTimeOffset.UtcNow.AddDays(1)
        });
        db.SaveChanges();

        var handler = new LogoutCommandHandler(db, new IdentityHasher());

        var result = await handler.Handle(new LogoutCommand("raw-2"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(db.RefreshTokens.IgnoreQueryFilters().Single().IsRevoked, "logout should revoke the token");
    }
}
