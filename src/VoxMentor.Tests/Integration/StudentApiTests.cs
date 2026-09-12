using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;
using Xunit;

namespace VoxMentor.Tests.Integration;

/// <summary>
/// Integration tests for the StudentController endpoints, covering both
/// documented authorization outcomes: 200 for Students, 403 for authenticated
/// non-Students (documented per CodeRabbit PR #59).
/// </summary>
public class StudentApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public StudentApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>Registers a user, logs in, and returns the access_token cookie pair.</summary>
    private async Task<(string CookieName, string CookieValue)> LoginAsStudentAsync(string email)
    {
        var password = "Password@123";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Student Api User",
            email,
            password
        });
        Assert.True(register.IsSuccessStatusCode, $"register failed: {(int)register.StatusCode}");

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var accessTokenCookie = cookies.FirstOrDefault(c => c.StartsWith("access_token="));
        Assert.NotNull(accessTokenCookie);
        return (CookieName: "access_token", CookieValue: accessTokenCookie.Split(';')[0]);
    }

    /// <summary>Creates an authenticated user with only the Admin role (no Student).</summary>
    private async Task<string> CreateNonStudentUserWithLoginAsync()
    {
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        const string password = "Password@123";

        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new IdentityRole("Admin"));
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = "Admin Api User"
        };
        var create = await userManager.CreateAsync(user, password);
        Assert.True(create.Succeeded, string.Join(';', create.Errors.Select(e => e.Description)));
        var addRole = await userManager.AddToRoleAsync(user, "Admin");
        Assert.True(addRole.Succeeded, string.Join(';', addRole.Errors.Select(e => e.Description)));

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var accessTokenCookie = cookies.FirstOrDefault(c => c.StartsWith("access_token="));
        Assert.NotNull(accessTokenCookie);
        return accessTokenCookie.Split(';')[0];
    }

    /// <summary>Verifies an authenticated user without the Student role receives 403 Forbidden.</summary>
    [Fact]
    public async Task Mastery_AuthenticatedNonStudent_Returns403Forbidden()
    {
        var accessTokenCookie = await CreateNonStudentUserWithLoginAsync();

        var message = new HttpRequestMessage(HttpMethod.Get, "/api/v1/student/mastery");
        message.Headers.Add("Cookie", accessTokenCookie);

        var response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Verifies a registered Student receives a successful mastery profile.</summary>
    [Fact]
    public async Task Mastery_StudentRole_Returns200()
    {
        var email = $"student-api-{Guid.NewGuid():N}@example.com";
        var (_, cookieValue) = await LoginAsStudentAsync(email);

        var message = new HttpRequestMessage(HttpMethod.Get, "/api/v1/student/mastery");
        message.Headers.Add("Cookie", cookieValue);

        var response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
    }
}
