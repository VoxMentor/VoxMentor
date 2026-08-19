using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Auth.Login;
using VoxMentor.Domain.Entities;
using RefreshTokenEntity = VoxMentor.Domain.Entities.RefreshToken;

namespace VoxMentor.Application.Features.Auth.RefreshToken;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, ApiResponse<LoginResultDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public RefreshTokenCommandHandler(
        IApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<ApiResponse<LoginResultDto>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new UnauthorizedAccessException("Refresh token is required.");
        }

        var existingToken = await _dbContext.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == request.RefreshToken, cancellationToken);

        if (existingToken == null || existingToken.IsRevoked || existingToken.ExpiryTime <= DateTimeOffset.UtcNow)
        {
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");
        }

        var user = existingToken.User;
        if (user == null)
        {
            throw new UnauthorizedAccessException("User not found.");
        }

        existingToken.IsRevoked = true;
        existingToken.RevokedAt = DateTimeOffset.UtcNow;

        var roles = await _userManager.GetRolesAsync(user);
        var (newAccessToken, accessExpiration) = _jwtTokenGenerator.GenerateAccessToken(user, roles);
        var (newRefreshToken, refreshExpiration) = _jwtTokenGenerator.GenerateRefreshToken();

        var newRefreshTokenEntity = new RefreshTokenEntity
        {
            UserId = user.Id,
            Token = newRefreshToken,
            ExpiryTime = refreshExpiration,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.RefreshTokens.Add(newRefreshTokenEntity);
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
            AccessToken = newAccessToken,
            AccessTokenExpiration = accessExpiration,
            RefreshToken = newRefreshToken,
            RefreshTokenExpiration = refreshExpiration
        };

        return ApiResponse<LoginResultDto>.SuccessResult(resultDto, "Token refreshed successfully.");
    }
}
