using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Auth.Logout;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IRefreshTokenHasher _refreshTokenHasher;

    public LogoutCommandHandler(IApplicationDbContext dbContext, IRefreshTokenHasher refreshTokenHasher)
    {
        _dbContext = dbContext;
        _refreshTokenHasher = refreshTokenHasher;
    }

    public async Task<ApiResponse<object>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var tokenHash = _refreshTokenHasher.Hash(request.RefreshToken);
            // Logout can arrive unauthenticated (expired access cookie); lookup
            // is by secret hash, so bypass the global user filter (#57).
            var tokenEntity = await _dbContext.RefreshTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

            if (tokenEntity != null && !tokenEntity.IsRevoked)
            {
                tokenEntity.IsRevoked = true;
                tokenEntity.RevokedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return ApiResponse<object>.SuccessResult(new { }, "Logged out successfully.");
    }
}
