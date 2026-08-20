using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;
using RefreshTokenEntity = VoxMentor.Domain.Entities.RefreshToken;

namespace VoxMentor.Application.Features.Auth.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, ApiResponse<LoginResultDto>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IRefreshTokenHasher _refreshTokenHasher;
    private readonly IApplicationDbContext _dbContext;

    public LoginCommandHandler(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJwtTokenGenerator jwtTokenGenerator,
        IRefreshTokenHasher refreshTokenHasher,
        IApplicationDbContext dbContext)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtTokenGenerator = jwtTokenGenerator;
        _refreshTokenHasher = refreshTokenHasher;
        _dbContext = dbContext;
    }

    public async Task<ApiResponse<LoginResultDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        var signInResult = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!signInResult.Succeeded)
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var (accessToken, accessExpiration) = _jwtTokenGenerator.GenerateAccessToken(user, roles);
        var (refreshToken, refreshExpiration) = _jwtTokenGenerator.GenerateRefreshToken();

        var activeTokens = await _dbContext.RefreshTokens
            .Where(t => t.UserId == user.Id && !t.IsRevoked && t.ExpiryTime > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var activeToken in activeTokens)
        {
            activeToken.IsRevoked = true;
            activeToken.RevokedAt = DateTimeOffset.UtcNow;
            activeToken.Version = Guid.NewGuid().ToString();
        }

        var refreshTokenEntity = new RefreshTokenEntity
        {
            UserId = user.Id,
            TokenHash = _refreshTokenHasher.Hash(refreshToken),
            ExpiryTime = refreshExpiration,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.RefreshTokens.Add(refreshTokenEntity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var userDto = new LoginResponseDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Roles = roles
        };

        var resultDto = new LoginResultDto
        {
            User = userDto,
            AccessToken = accessToken,
            AccessTokenExpiration = accessExpiration,
            RefreshToken = refreshToken,
            RefreshTokenExpiration = refreshExpiration
        };

        return ApiResponse<LoginResultDto>.SuccessResult(resultDto, "Login successful.");
    }
}
