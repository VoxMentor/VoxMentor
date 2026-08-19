using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Auth.Login;
using Xunit;

namespace VoxMentor.Tests.Integration;

public class LoginApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LoginApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200OK_SetsHttpOnlyCookies_WithoutTokensInJsonResponse()
    {
        var registerRequest = new
        {
            fullName = "Cookie Test User",
            email = "cookietest1@example.com",
            password = "Password@123"
        };
        var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);

        var loginRequest = new
        {
            email = "cookietest1@example.com",
            password = "Password@123"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var jsonContent = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", jsonContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", jsonContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", jsonContent, StringComparison.OrdinalIgnoreCase);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponseDto>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("cookietest1@example.com", result.Data.Email);

        // Verify Set-Cookie header contains both access_token and refresh_token
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.Contains("access_token=") && c.Contains("httponly"));
        Assert.Contains(cookies, c => c.Contains("refresh_token=") && c.Contains("httponly"));
    }

    [Fact]
    public async Task RefreshToken_ValidCookie_ReturnsNewCookies()
    {
        var registerRequest = new
        {
            fullName = "Refresh Test User",
            email = "refreshtest@example.com",
            password = "Password@123"
        };
        await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);

        var loginRequest = new
        {
            email = "refreshtest@example.com",
            password = "Password@123"
        };
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookieHeader = cookies.FirstOrDefault(c => c.StartsWith("refresh_token="));
        Assert.NotNull(refreshCookieHeader);

        var refreshTokenValue = refreshCookieHeader.Split(';')[0]; // "refresh_token=xxx"

        var refreshMessage = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refreshMessage.Headers.Add("Cookie", refreshTokenValue);

        var refreshResponse = await _client.SendAsync(refreshMessage);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        Assert.True(refreshResponse.Headers.Contains("Set-Cookie"));
        var newCookies = refreshResponse.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(newCookies, c => c.Contains("access_token=") && c.Contains("httponly"));
        Assert.Contains(newCookies, c => c.Contains("refresh_token=") && c.Contains("httponly"));
    }

    [Fact]
    public async Task RefreshToken_InvalidCookie_Returns401Unauthorized()
    {
        var refreshMessage = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refreshMessage.Headers.Add("Cookie", "refresh_token=invalid_token_value");

        var response = await _client.SendAsync(refreshMessage);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_InvalidPassword_Returns401Unauthorized()
    {
        var registerRequest = new
        {
            fullName = "Wrong Pass User",
            email = "wrongpass@example.com",
            password = "Password@123"
        };
        await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);

        var loginRequest = new
        {
            email = "wrongpass@example.com",
            password = "WrongPassword@123"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ClearsBothAccessTokenAndRefreshTokenCookies()
    {
        var response = await _client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.Contains("access_token="));
        Assert.Contains(cookies, c => c.Contains("refresh_token="));
    }
}
