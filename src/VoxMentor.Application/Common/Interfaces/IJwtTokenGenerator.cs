using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    (string Token, DateTimeOffset Expiration) GenerateAccessToken(ApplicationUser user, IList<string> roles);
    (string Token, DateTimeOffset Expiration) GenerateRefreshToken();
}
