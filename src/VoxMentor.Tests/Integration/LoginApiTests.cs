using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Auth.Login;
using Xunit;

namespace VoxMentor.Tests.Integration;

public class LoginApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LoginApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
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
    public async Task RefreshToken_ConcurrentRequests_ExactlyOneSucceeds()
    {
        var registerRequest = new
        {
            fullName = "Concurrent Refresh User",
            email = "concurrentrefresh@example.com",
            password = "Password@123"
        };
        await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);

        var loginRequest = new
        {
            email = "concurrentrefresh@example.com",
            password = "Password@123"
        };
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookieHeader = cookies.FirstOrDefault(c => c.StartsWith("refresh_token="));
        Assert.NotNull(refreshCookieHeader);
        var refreshTokenValue = refreshCookieHeader.Split(';')[0];

        var firstMessage = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        firstMessage.Headers.Add("Cookie", refreshTokenValue);
        var secondMessage = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        secondMessage.Headers.Add("Cookie", refreshTokenValue);

        var results = await Task.WhenAll(
            _client.SendAsync(firstMessage),
            _client.SendAsync(secondMessage));

        var statusCodes = results.Select(r => r.StatusCode).OrderBy(s => s).ToList();
        Assert.Equal(2, statusCodes.Count);
        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Unauthorized, statusCodes);
        Assert.Equal(1, statusCodes.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(1, statusCodes.Count(s => s == HttpStatusCode.Unauthorized));
    }

    // #57: login revocation must reach the returning user's other sessions
    // even when the login request itself is unauthenticated (expired access
    // cookie) — without IgnoreQueryFilters the user filter hides them and
    // the old refresh token stays alive.
    [Fact]
    public async Task Login_FromCookielessClient_RevokesPreviousSession()
    {
        var email = $"revoke-{Guid.NewGuid():N}@example.com";
        var loginRequest = new { email, password = "Password@123" };

        // Session A: registers and logs in; its client keeps session-1 cookies.
        var clientA = _factory.CreateClient();
        await clientA.PostAsJsonAsync("/api/v1/auth/register", new { fullName = "Revoke Test User", email, password = "Password@123" });
        var firstLogin = await clientA.PostAsJsonAsync("/api/v1/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);

        // Session B: cookieless client = login with an expired access token.
        var clientB = _factory.CreateClient();
        var secondLogin = await clientB.PostAsJsonAsync("/api/v1/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);

        // Session A's refresh token must be dead now. (Manual Cookie headers
        // are shadowed by the client's cookie store, so clientA itself is the
        // only way to present session 1's token.)
        var refreshResponse = await clientA.PostAsync("/api/v1/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
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
