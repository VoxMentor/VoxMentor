using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    (string Token, DateTime Expiration) GenerateAccessToken(ApplicationUser user, IList<string> roles);
    (string Token, DateTimeOffset Expiration) GenerateRefreshToken();
}
