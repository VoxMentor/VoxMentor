using System.Net;
using System.Net.Http.Json;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Auth.Login;
using Xunit;

namespace VoxMentor.Tests.Integration;

public class MeApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MeApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Me_NoAuth_Returns401Unauthorized()
    {
        var response = await _client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ValidCookie_Returns200_WithUser()
    {
        var registerRequest = new
        {
            fullName = "Me Endpoint User",
            email = "meendpoint@example.com",
            password = "Password@123"
        };
        await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);

        var loginRequest = new
        {
            email = "meendpoint@example.com",
            password = "Password@123"
        };
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var accessTokenCookie = cookies.FirstOrDefault(c => c.StartsWith("access_token="));
        Assert.NotNull(accessTokenCookie);
        var accessTokenValue = accessTokenCookie.Split(';')[0];

        var meMessage = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        meMessage.Headers.Add("Cookie", accessTokenValue);

        var meResponse = await _client.SendAsync(meMessage);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var result = await meResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponseDto>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("meendpoint@example.com", result.Data.Email);
        Assert.Equal("Me Endpoint User", result.Data.FullName);
        Assert.Contains("Student", result.Data.Roles);
    }
}
