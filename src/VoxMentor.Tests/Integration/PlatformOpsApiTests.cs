using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Api.Controllers;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using Xunit;

namespace VoxMentor.Tests.Integration;

/// <summary>
/// Platform ops tests for issue #93: plagiarism review queue and cross-user tutor
/// support under the ManagePlatform policy. Role matrix mirrors AdminRbacApiTests.
/// </summary>
public class PlatformOpsApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public PlatformOpsApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        // HandleCookies=false: explicit Cookie header per request (same as AdminRbacApiTests).
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

    private async Task<Guid> SeedSubmissionAsync(string userId, float score, string code = "print('hello')")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var submission = new CodeSubmission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            QuestionId = Guid.NewGuid(),
            Code = code,
            Language = "python",
            IsCorrect = true,
            TestCasesPassed = 3,
            TestCasesTotal = 3,
            PlagiarismScore = score,
            Status = SubmissionStatus.Accepted
        };
        db.CodeSubmissions.Add(submission);
        await db.SaveChangesAsync();
        return submission.Id;
    }

    private async Task<Guid> SeedSessionAsync(string userId, TutorSessionStatus status, string question, string? answer = "an answer")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var session = new TutorSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Question = question,
            Answer = answer,
            Status = status,
            TotalTokens = 42
        };
        db.TutorSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private async Task AssertGetAsync(string url, string role, HttpStatusCode expected)
    {
        var (cookie, _) = await LoginWithRoleAsync(role);
        var response = await _client.SendAsync(Request(HttpMethod.Get, url, cookie));
        Assert.Equal(expected, response.StatusCode);
    }

    // --- Role matrix: ManagePlatform gates all four endpoints ---

    [Fact]
    public async Task Endpoints_Unauthenticated_Return401Unauthorized()
    {
        foreach (var url in new[]
        {
            "/api/v1/admin/plagiarism/submissions",
            "/api/v1/admin/plagiarism/submissions/" + Guid.NewGuid(),
            "/api/v1/admin/tutor/sessions",
            "/api/v1/admin/tutor/sessions/" + Guid.NewGuid()
        })
        {
            var response = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task PlagiarismList_GatesRoles()
    {
        const string url = "/api/v1/admin/plagiarism/submissions";
        await AssertGetAsync(url, "PlatformAdmin", HttpStatusCode.OK);
        await AssertGetAsync(url, "SuperAdmin", HttpStatusCode.OK);
        await AssertGetAsync(url, "ContentAdmin", HttpStatusCode.Forbidden);
        await AssertGetAsync(url, "Student", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PlagiarismDetail_GatesRoles()
    {
        var (_, studentId) = await LoginWithRoleAsync("Student");
        var id = await SeedSubmissionAsync(studentId, 0.95f);
        var url = $"/api/v1/admin/plagiarism/submissions/{id}";
        await AssertGetAsync(url, "PlatformAdmin", HttpStatusCode.OK);
        await AssertGetAsync(url, "SuperAdmin", HttpStatusCode.OK);
        await AssertGetAsync(url, "ContentAdmin", HttpStatusCode.Forbidden);
        await AssertGetAsync(url, "Student", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TutorSessionsList_GatesRoles()
    {
        const string url = "/api/v1/admin/tutor/sessions";
        await AssertGetAsync(url, "PlatformAdmin", HttpStatusCode.OK);
        await AssertGetAsync(url, "SuperAdmin", HttpStatusCode.OK);
        await AssertGetAsync(url, "ContentAdmin", HttpStatusCode.Forbidden);
        await AssertGetAsync(url, "Student", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TutorSessionDetail_GatesRoles()
    {
        var (_, studentId) = await LoginWithRoleAsync("Student");
        var id = await SeedSessionAsync(studentId, TutorSessionStatus.Completed, "gate check");
        var url = $"/api/v1/admin/tutor/sessions/{id}";
        await AssertGetAsync(url, "PlatformAdmin", HttpStatusCode.OK);
        await AssertGetAsync(url, "SuperAdmin", HttpStatusCode.OK);
        await AssertGetAsync(url, "ContentAdmin", HttpStatusCode.Forbidden);
        await AssertGetAsync(url, "Student", HttpStatusCode.Forbidden);
    }

    // --- Functional: queue filtering, detail payloads, 404s, cross-user filters ---

    [Fact]
    public async Task PlagiarismQueue_FiltersByMinScore_ReturnsDetail()
    {
        var (adminCookie, _) = await LoginWithRoleAsync("PlatformAdmin");
        var (_, studentId) = await LoginWithRoleAsync("Student");
        var highId = await SeedSubmissionAsync(studentId, 0.95f, "def solve(): return 42");
        var lowId = await SeedSubmissionAsync(studentId, 0.6f);

        // default minScore (0.7): high included, low excluded, sorted by score desc
        var listResponse = await _client.SendAsync(
            Request(HttpMethod.Get, "/api/v1/admin/plagiarism/submissions", adminCookie));
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<ApiResponse<List<PlagiarismQueueItemDto>>>();
        Assert.NotNull(list);
        Assert.True(list.Success);
        Assert.Contains(list.Data!, i => i.Id == highId);
        Assert.DoesNotContain(list.Data!, i => i.Id == lowId);
        Assert.All(list.Data!, i => Assert.True(i.PlagiarismScore >= 0.7f));
        var scores = list.Data!.Select(i => i.PlagiarismScore!.Value).ToList();
        Assert.True(scores.SequenceEqual(scores.OrderByDescending(s => s)));

        // raised threshold excludes the high-scoring row
        var strictResponse = await _client.SendAsync(
            Request(HttpMethod.Get, "/api/v1/admin/plagiarism/submissions?minScore=0.99", adminCookie));
        var strict = await strictResponse.Content.ReadFromJsonAsync<ApiResponse<List<PlagiarismQueueItemDto>>>();
        Assert.NotNull(strict);
        Assert.DoesNotContain(strict.Data!, i => i.Id == highId);

        // detail includes source code and a user email; unknown id is 404
        var detailResponse = await _client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/plagiarism/submissions/{highId}", adminCookie));
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<ApiResponse<PlagiarismSubmissionDetailDto>>();
        Assert.NotNull(detail);
        Assert.Equal("def solve(): return 42", detail.Data!.Code);
        Assert.False(string.IsNullOrEmpty(detail.Data.UserEmail));
        Assert.Equal(0.95f, detail.Data.PlagiarismScore);

        var missingResponse = await _client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/plagiarism/submissions/{Guid.NewGuid()}", adminCookie));
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        // sanity: low-scoring row is reachable by id (excluded from queue only)
        var lowDetail = await _client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/plagiarism/submissions/{lowId}", adminCookie));
        Assert.Equal(HttpStatusCode.OK, lowDetail.StatusCode);
    }

    [Fact]
    public async Task TutorSessions_CrossUserFilters_ReturnDetail404()
    {
        var (adminCookie, _) = await LoginWithRoleAsync("PlatformAdmin");
        var (_, studentA) = await LoginWithRoleAsync("Student");
        var (_, studentB) = await LoginWithRoleAsync("Student");
        var aCompleted = await SeedSessionAsync(studentA, TutorSessionStatus.Completed, "what is a heap?", "a heap is ...");
        var aFailed = await SeedSessionAsync(studentA, TutorSessionStatus.Failed, "why does this fail?");
        await SeedSessionAsync(studentB, TutorSessionStatus.Completed, "b question");

        // userId filter: only student A's two sessions
        var byUserResponse = await _client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/tutor/sessions?userId={studentA}", adminCookie));
        Assert.Equal(HttpStatusCode.OK, byUserResponse.StatusCode);
        var byUser = await byUserResponse.Content.ReadFromJsonAsync<ApiResponse<List<TutorSessionSummaryDto>>>();
        Assert.NotNull(byUser);
        Assert.True(byUser.Success);
        Assert.Equal(2, byUser.Data!.Count);
        Assert.Contains(byUser.Data!, s => s.Id == aCompleted);
        Assert.Contains(byUser.Data!, s => s.Id == aFailed);

        // userId + status: only the completed one
        var filteredResponse = await _client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/tutor/sessions?userId={studentA}&status=Completed", adminCookie));
        var filtered = await filteredResponse.Content.ReadFromJsonAsync<ApiResponse<List<TutorSessionSummaryDto>>>();
        Assert.NotNull(filtered);
        Assert.Single(filtered.Data!);
        Assert.Equal(aCompleted, filtered.Data![0].Id);

        // status filter alone still sees student B's completed session
        var byStatusResponse = await _client.SendAsync(
            Request(HttpMethod.Get, "/api/v1/admin/tutor/sessions?status=Completed", adminCookie));
        var byStatus = await byStatusResponse.Content.ReadFromJsonAsync<ApiResponse<List<TutorSessionSummaryDto>>>();
        Assert.NotNull(byStatus);
        Assert.Contains(byStatus.Data!, s => s.Id == aCompleted);

        // detail: full answer present, unknown id is 404
        var detailResponse = await _client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/tutor/sessions/{aCompleted}", adminCookie));
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<ApiResponse<TutorSessionDetailDto>>();
        Assert.NotNull(detail);
        Assert.Equal("a heap is ...", detail.Data!.Answer);
        Assert.False(string.IsNullOrEmpty(detail.Data.UserEmail));

        var missingResponse = await _client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/tutor/sessions/{Guid.NewGuid()}", adminCookie));
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }
}
