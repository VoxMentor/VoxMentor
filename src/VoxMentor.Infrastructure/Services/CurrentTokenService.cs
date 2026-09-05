using Microsoft.AspNetCore.Http;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Reads the authenticated user's raw access token from the request (the
/// "access_token" cookie used by the API, or an Authorization bearer header)
/// so it can be forwarded to internal microservices.
/// </summary>
public class CurrentTokenService : ICurrentTokenService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentTokenService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public string? AccessToken
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context is null)
                return null;

            if (context.Request.Cookies.TryGetValue("access_token", out var cookieToken)
                && !string.IsNullOrWhiteSpace(cookieToken))
            {
                return cookieToken;
            }

            var header = context.Request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var bearer = header["Bearer ".Length..].Trim();
                return bearer.Length > 0 ? bearer : null;
            }

            return null;
        }
    }
}
