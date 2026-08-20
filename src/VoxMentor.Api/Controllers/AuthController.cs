using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Auth.Login;
using VoxMentor.Application.Features.Auth.Logout;
using VoxMentor.Application.Features.Auth.RefreshToken;
using VoxMentor.Application.Features.Auth.Register;

namespace VoxMentor.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command, CancellationToken cancellationToken)
    {
        var response = await _sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken cancellationToken)
    {
        var response = await _sender.Send(command, cancellationToken);

        if (response.Success && response.Data != null)
        {
            SetTokenCookies(response.Data.AccessToken, response.Data.AccessTokenExpiration, response.Data.RefreshToken, response.Data.RefreshTokenExpiration);
            var userResponse = ApiResponse<LoginResponseDto>.SuccessResult(response.Data.User, response.Message);
            return Ok(userResponse);
        }

        return Ok(response);
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies["refresh_token"];
        var command = new RefreshTokenCommand(refreshToken ?? string.Empty);
        var response = await _sender.Send(command, cancellationToken);

        if (response.Success && response.Data != null)
        {
            SetTokenCookies(response.Data.AccessToken, response.Data.AccessTokenExpiration, response.Data.RefreshToken, response.Data.RefreshTokenExpiration);
            var userResponse = ApiResponse<LoginResponseDto>.SuccessResult(response.Data.User, response.Message);
            return Ok(userResponse);
        }

        return Ok(response);
    }

    [HttpPost("logout")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies["refresh_token"];
        var command = new LogoutCommand(refreshToken);
        var response = await _sender.Send(command, cancellationToken);

        Response.Cookies.Delete("access_token", new CookieOptions { Path = "/api/v1" });
        Response.Cookies.Delete("refresh_token", new CookieOptions { Path = "/api/v1/auth" });

        return Ok(response);
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value
                    ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value;
        var name = User.FindFirst(ClaimTypes.Name)?.Value
                   ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Name)?.Value;
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var user = new LoginResponseDto
        {
            Id = userId,
            FullName = name ?? string.Empty,
            Email = email ?? string.Empty,
            Roles = roles
        };

        return Ok(ApiResponse<LoginResponseDto>.SuccessResult(user));
    }

    private void SetTokenCookies(string accessToken, DateTimeOffset accessExpiration, string refreshToken, DateTimeOffset refreshExpiration)
    {
        var accessCookieOptions = new CookieOptions
        {
            Path = "/api/v1",
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = accessExpiration
        };

        var refreshCookieOptions = new CookieOptions
        {
            Path = "/api/v1/auth",
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = refreshExpiration
        };

        Response.Cookies.Append("access_token", accessToken, accessCookieOptions);
        Response.Cookies.Append("refresh_token", refreshToken, refreshCookieOptions);
    }
}
