using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace VoxMentor.Tests.Integration;

/// <summary>
/// Authz coverage for the #70 textbook endpoints: 401 for anonymous callers,
/// 403 for authenticated users without the Admin role (mirrors StudentApiTests).
/// </summary>
public class TextbookApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public TextbookApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>Registers a Student, logs in, returns the access_token cookie pair.</summary>
    private async Task<string> LoginAsStudentAsync(string email)
    {
        var password = "Password@123";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Textbook Api Student",
            email,
            password
        });
        Assert.True(register.IsSuccessStatusCode, $"register failed: {(int)register.StatusCode}");

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var accessTokenCookie = cookies.FirstOrDefault(c => c.StartsWith("access_token="));
        Assert.NotNull(accessTokenCookie);
        return accessTokenCookie.Split(';')[0];
    }

    private static MultipartFormDataContent UploadBody()
    {
        var body = new MultipartFormDataContent();
        body.Add(new StringContent("integration test content"), "file", "textbook.txt");
        return body;
    }

    [Fact]
    public async Task Upload_Anonymous_Returns401Unauthorized()
    {
        var response = await _client.PostAsync("/api/v1/admin/textbook/upload", UploadBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Status_Anonymous_Returns401Unauthorized()
    {
        var response = await _client.GetAsync($"/api/v1/admin/textbook/status/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_AuthenticatedStudent_Returns403Forbidden()
    {
        var cookie = await LoginAsStudentAsync($"textbook-student-{Guid.NewGuid():N}@example.com");

        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/textbook/upload")
        {
            Content = UploadBody()
        };
        message.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Status_AuthenticatedStudent_Returns403Forbidden()
    {
        var cookie = await LoginAsStudentAsync($"textbook-student-{Guid.NewGuid():N}@example.com");

        var message = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/admin/textbook/status/{Guid.NewGuid()}");
        message.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
