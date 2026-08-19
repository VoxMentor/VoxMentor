using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Authentication;

public class RefreshTokenHasher : IRefreshTokenHasher
{
    private readonly byte[] _key;

    public RefreshTokenHasher(IOptions<JwtSettings> jwtOptions)
    {
        _key = Encoding.UTF8.GetBytes(jwtOptions.Value.Secret);
    }

    public string Hash(string token)
    {
        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }
}
