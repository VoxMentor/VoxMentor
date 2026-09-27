using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Api.Controllers;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;
using Xunit;

namespace VoxMentor.Tests.Integration;

/// <summary>
/// RBAC tests for issue #82: named policies (ManageContent / ManagePlatform /
/// ManageRoles), SuperAdmin-only role management, the last-SuperAdmin lockout
/// guard, and per-role gating of platform surfaces.
/// </summary>
public class AdminRbacApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public AdminRbacApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        // HandleCookies=false: these tests log in as several roles and send the
        // desired token via an explicit Cookie header — the shared cookie jar
        // would otherwise replay the most recent login's token and break 403s.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    /// <summary>Creates a user holding only the given role, logs in, returns (cookie, userId).</summary>
    private async Task<(string Cookie, string UserId)> LoginWithRoleAsync(string role)
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        const string password = "Password@123";

        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = $"{role} Api User"
        };
        var create = await userManager.CreateAsync(user, password);
        Assert.True(create.Succeeded, string.Join(';', create.Errors.Select(e => e.Description)));
        var addRole = await userManager.AddToRoleAsync(user, role);
        Assert.True(addRole.Succeeded, string.Join(';', addRole.Errors.Select(e => e.Description)));
        var userId = user.Id;

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var accessTokenCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.StartsWith("access_token="));
        Assert.NotNull(accessTokenCookie);
        return (accessTokenCookie.Split(';')[0], userId);
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string cookie, object? body = null)
    {
        var message = new HttpRequestMessage(method, url);
        message.Headers.Add("Cookie", cookie);
        if (body is not null)
        {
            message.Content = JsonContent.Create(body);
        }
        return message;
    }

    // --- ManageRoles: role management is SuperAdmin-only ---

    [Fact]
    public async Task RolesList_SuperAdmin_Returns200_WithAllSeededRoles()
    {
        var (cookie, _) = await LoginWithRoleAsync("SuperAdmin");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/roles", cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<string>>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Contains("Student", result.Data!);
        Assert.Contains("ContentAdmin", result.Data!);
        Assert.Contains("PlatformAdmin", result.Data!);
        Assert.Contains("SuperAdmin", result.Data!);
    }

    [Fact]
    public async Task RolesList_ContentAdmin_Returns403Forbidden()
    {
        var (cookie, _) = await LoginWithRoleAsync("ContentAdmin");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/roles", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RolesList_PlatformAdmin_Returns403Forbidden()
    {
        var (cookie, _) = await LoginWithRoleAsync("PlatformAdmin");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/roles", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RolesList_Student_Returns403Forbidden()
    {
        var (cookie, _) = await LoginWithRoleAsync("Student");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/roles", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AssignRole_SuperAdmin_Returns200_AndPersistsRole()
    {
        var (adminCookie, _) = await LoginWithRoleAsync("SuperAdmin");
        var (_, targetUserId) = await LoginWithRoleAsync("Student");

        var response = await _client.SendAsync(Request(
            HttpMethod.Post, $"/api/v1/admin/users/{targetUserId}/roles", adminCookie,
            new { role = "ContentAdmin" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<UserRolesDto>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Contains("ContentAdmin", result.Data!.Roles);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var target = await userManager.FindByIdAsync(targetUserId);
        Assert.NotNull(target);
        Assert.True(await userManager.IsInRoleAsync(target, "ContentAdmin"));
    }

    [Fact]
    public async Task RemoveRole_SuperAdmin_Returns200_AndPersistsRemoval()
    {
        var (adminCookie, _) = await LoginWithRoleAsync("SuperAdmin");
        var (_, targetUserId) = await LoginWithRoleAsync("Student");
        var assign = await _client.SendAsync(Request(
            HttpMethod.Post, $"/api/v1/admin/users/{targetUserId}/roles", adminCookie,
            new { role = "ContentAdmin" }));
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        var response = await _client.SendAsync(Request(
            HttpMethod.Delete, $"/api/v1/admin/users/{targetUserId}/roles/ContentAdmin", adminCookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var target = await userManager.FindByIdAsync(targetUserId);
        Assert.NotNull(target);
        Assert.False(await userManager.IsInRoleAsync(target, "ContentAdmin"));
    }

    [Fact]
    public async Task RemoveLastSuperAdmin_Returns400BadRequest()
    {
        var (cookie, userId) = await LoginWithRoleAsync("SuperAdmin");

        // This class shares one database; demote every other SuperAdmin so the
        // target is provably the last one regardless of test execution order.
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var superAdmins = await userManager.GetUsersInRoleAsync("SuperAdmin");
            foreach (var superAdmin in superAdmins)
            {
                if (superAdmin.Id != userId)
                {
                    await userManager.RemoveFromRoleAsync(superAdmin, "SuperAdmin");
                }
            }
        }

        var response = await _client.SendAsync(Request(
            HttpMethod.Delete, $"/api/v1/admin/users/{userId}/roles/SuperAdmin", cookie));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("last SuperAdmin", result.Message);
    }

    [Fact]
    public async Task AssignRole_UnknownRole_Returns400BadRequest()
    {
        var (cookie, targetUserId) = await LoginWithRoleAsync("SuperAdmin");

        var response = await _client.SendAsync(Request(
            HttpMethod.Post, $"/api/v1/admin/users/{targetUserId}/roles", cookie,
            new { role = "BillingAdmin" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- ManageContent: content endpoints ---

    [Fact]
    public async Task Questions_ContentAdmin_Returns200Ok()
    {
        var (cookie, _) = await LoginWithRoleAsync("ContentAdmin");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/questions", cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Questions_PlatformAdmin_Returns403Forbidden()
    {
        var (cookie, _) = await LoginWithRoleAsync("PlatformAdmin");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/questions", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Questions_Student_Returns403Forbidden()
    {
        var (cookie, _) = await LoginWithRoleAsync("Student");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/questions", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- ManagePlatform: audit log read ---

    [Fact]
    public async Task AuditLogs_PlatformAdmin_Returns200Ok()
    {
        var (cookie, _) = await LoginWithRoleAsync("PlatformAdmin");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/audit-logs", cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<AuditLog>>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task AuditLogs_ContentAdmin_Returns403Forbidden()
    {
        var (cookie, _) = await LoginWithRoleAsync("ContentAdmin");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/audit-logs", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuditLogs_Student_Returns403Forbidden()
    {
        var (cookie, _) = await LoginWithRoleAsync("Student");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/audit-logs", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- ManagePlatform: health detail ---

    [Fact]
    public async Task HealthDetail_Anonymous_Returns401Unauthorized()
    {
        var response = await _client.GetAsync("/health/detail");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthDetail_PlatformAdmin_Returns200Ok()
    {
        var (cookie, _) = await LoginWithRoleAsync("PlatformAdmin");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/health/detail", cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthDetail_Student_Returns403Forbidden()
    {
        var (cookie, _) = await LoginWithRoleAsync("Student");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/health/detail", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- #82 review fixes: coverage gaps, overflow guard, stale-token replay ---

    [Fact]
    public async Task UsersList_SuperAdmin_Returns200_WithUsers()
    {
        var (cookie, _) = await LoginWithRoleAsync("SuperAdmin");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/users", cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<UserRolesDto>>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotEmpty(result.Data!);
    }

    [Fact]
    public async Task UsersList_Anonymous_Returns401Unauthorized()
    {
        var response = await _client.GetAsync("/api/v1/admin/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("ContentAdmin")]
    [InlineData("PlatformAdmin")]
    [InlineData("Student")]
    public async Task AssignRole_NonSuperAdmin_Returns403Forbidden(string role)
    {
        var (cookie, userId) = await LoginWithRoleAsync(role);

        var response = await _client.SendAsync(Request(
            HttpMethod.Post, $"/api/v1/admin/users/{userId}/roles", cookie,
            new { role = "ContentAdmin" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("ContentAdmin")]
    [InlineData("PlatformAdmin")]
    [InlineData("Student")]
    public async Task RemoveRole_NonSuperAdmin_Returns403Forbidden(string role)
    {
        var (cookie, userId) = await LoginWithRoleAsync(role);

        var response = await _client.SendAsync(Request(
            HttpMethod.Delete, $"/api/v1/admin/users/{userId}/roles/Student", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Questions_SuperAdmin_Returns200Ok()
    {
        var (cookie, _) = await LoginWithRoleAsync("SuperAdmin");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/questions", cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuditLogs_SuperAdmin_Returns200Ok()
    {
        var (cookie, _) = await LoginWithRoleAsync("SuperAdmin");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/audit-logs", cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UsersList_PageOverflow_Returns200Empty()
    {
        var (cookie, _) = await LoginWithRoleAsync("SuperAdmin");

        var response = await _client.SendAsync(
            Request(HttpMethod.Get, "/api/v1/admin/users?page=2147483647&pageSize=50", cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<UserRolesDto>>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task AuditLogs_PageOverflow_Returns200Empty()
    {
        var (cookie, _) = await LoginWithRoleAsync("PlatformAdmin");

        var response = await _client.SendAsync(
            Request(HttpMethod.Get, "/api/v1/admin/audit-logs?page=2147483647&pageSize=50", cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<AuditLog>>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task RemoveLastSuperAdmin_LowercaseRole_Returns400BadRequest()
    {
        var (cookie, userId) = await LoginWithRoleAsync("SuperAdmin");

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var superAdmins = await userManager.GetUsersInRoleAsync("SuperAdmin");
            foreach (var superAdmin in superAdmins)
            {
                if (superAdmin.Id != userId)
                {
                    await userManager.RemoveFromRoleAsync(superAdmin, "SuperAdmin");
                }
            }
        }

        var response = await _client.SendAsync(Request(
            HttpMethod.Delete, $"/api/v1/admin/users/{userId}/roles/superadmin", cookie));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.NotNull(result);
        Assert.Contains("last SuperAdmin", result.Message);
    }

    [Fact]
    public async Task RemoveOneOfTwoSuperAdmins_Returns200_AndPersistsRemoval()
    {
        var (cookieA, _) = await LoginWithRoleAsync("SuperAdmin");
        var (_, userIdB) = await LoginWithRoleAsync("SuperAdmin");

        var response = await _client.SendAsync(Request(
            HttpMethod.Delete, $"/api/v1/admin/users/{userIdB}/roles/SuperAdmin", cookieA));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var userB = await userManager.FindByIdAsync(userIdB);
        Assert.NotNull(userB);
        Assert.False(await userManager.IsInRoleAsync(userB, "SuperAdmin"));
    }

    [Fact]
    public async Task DemotedAdmin_StaleToken_CannotReElevate()
    {
        var (cookieA, userIdA) = await LoginWithRoleAsync("SuperAdmin");
        await LoginWithRoleAsync("SuperAdmin"); // second SuperAdmin so the self-demote passes the guard

        var selfDemote = await _client.SendAsync(Request(
            HttpMethod.Delete, $"/api/v1/admin/users/{userIdA}/roles/SuperAdmin", cookieA));
        Assert.Equal(HttpStatusCode.OK, selfDemote.StatusCode);

        // The demoted admin's still-unexpired token must now fail authentication...
        var replay = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/roles", cookieA));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // ...and the #82 exploit (re-assign SuperAdmin to self) must not land.
        var exploit = await _client.SendAsync(Request(
            HttpMethod.Post, $"/api/v1/admin/users/{userIdA}/roles", cookieA,
            new { role = "SuperAdmin" }));
        Assert.Equal(HttpStatusCode.Unauthorized, exploit.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var userA = await userManager.FindByIdAsync(userIdA);
        Assert.NotNull(userA);
        Assert.False(await userManager.IsInRoleAsync(userA, "SuperAdmin"));
    }
}
