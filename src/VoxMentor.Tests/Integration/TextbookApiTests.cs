using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Admin.GetTextbookJob;
using VoxMentor.Application.Features.Admin.UploadTextbook;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;
using Xunit;

namespace VoxMentor.Tests.Integration;

/// <summary>
/// #70/#104 textbook endpoints: authz short-circuits, upload success path,
/// idempotent retry reuse, and validation/not-found edges (queue is recorded,
/// not Hangfire — see CustomWebApplicationFactory).
/// </summary>
public class TextbookApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public TextbookApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        // HandleCookies=false: the success-path tests log in and send the token
        // explicitly, matching AdminRbacApiTests.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    /// <summary>Creates a ContentAdmin, logs in, returns the access_token cookie pair.</summary>
    private async Task<string> LoginAsContentAdminAsync()
    {
        const string role = "ContentAdmin";
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        const string password = "Password@123";

        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Textbook Content Admin" };
        var create = await userManager.CreateAsync(user, password);
        Assert.True(create.Succeeded, string.Join(';', create.Errors.Select(e => e.Description)));
        var addRole = await userManager.AddToRoleAsync(user, role);
        Assert.True(addRole.Succeeded, string.Join(';', addRole.Errors.Select(e => e.Description)));

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var accessTokenCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.StartsWith("access_token="));
        Assert.NotNull(accessTokenCookie);
        return accessTokenCookie.Split(';')[0];
    }

    private static MultipartFormDataContent UploadBody(
        string content = "integration test content", string fileName = "textbook.txt")
    {
        var body = new MultipartFormDataContent();
        body.Add(new StringContent(content), "file", fileName);
        return body;
    }

    private async Task<HttpResponseMessage> UploadAsync(
        string cookie, string content, string fileName = "textbook.txt")
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/textbook/upload")
        {
            Content = UploadBody(content, fileName)
        };
        message.Headers.Add("Cookie", cookie);
        return await _client.SendAsync(message);
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

    [Fact]
    public async Task Upload_ContentAdmin_Returns202_AndPollablePendingJob()
    {
        var cookie = await LoginAsContentAdminAsync();
        var queue = _factory.Services.GetRequiredService<RecordingTextbookIngestionQueue>();
        var enqueuedBefore = queue.Enqueued.Count;
        var content = $"success path {Guid.NewGuid():N}";

        var response = await UploadAsync(cookie, content);

        try
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<UploadTextbookResultDto>>();
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.NotEqual(Guid.Empty, result.Data.JobId);
            Assert.Contains(result.Data.JobId.ToString(), result.Data.Message);

            // The upload queued exactly one ingestion for the new job.
            var enqueued = queue.Enqueued.Skip(enqueuedBefore).ToList();
            var record = Assert.Single(enqueued);
            Assert.Equal(result.Data.JobId, record.JobId);
            Assert.True(File.Exists(record.FilePath), $"staged file missing: {record.FilePath}");

            // The returned id resolves through the status endpoint the UI polls.
            var statusMessage = new HttpRequestMessage(
                HttpMethod.Get, $"/api/v1/admin/textbook/status/{result.Data.JobId}");
            statusMessage.Headers.Add("Cookie", cookie);
            var statusResponse = await _client.SendAsync(statusMessage);

            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            var status = await statusResponse.Content.ReadFromJsonAsync<ApiResponse<TextbookJobDto>>();
            Assert.NotNull(status);
            Assert.True(status.Success);
            Assert.NotNull(status.Data);
            Assert.Equal(result.Data.JobId, status.Data.JobId);
            Assert.Equal("textbook.txt", status.Data.FileName);
            Assert.Equal(TextbookJobStatus.Pending, status.Data.Status);
        }
        finally
        {
            foreach (var (jobId, filePath) in queue.Enqueued.Skip(enqueuedBefore))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task Upload_IdempotentRetry_ReusesJobId_AndQueuesOnce()
    {
        var cookie = await LoginAsContentAdminAsync();
        var queue = _factory.Services.GetRequiredService<RecordingTextbookIngestionQueue>();
        var enqueuedBefore = queue.Enqueued.Count;
        var content = $"idempotent retry {Guid.NewGuid():N}";

        var first = await UploadAsync(cookie, content);
        var second = await UploadAsync(cookie, content);

        try
        {
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

            var firstResult = await first.Content.ReadFromJsonAsync<ApiResponse<UploadTextbookResultDto>>();
            var secondResult = await second.Content.ReadFromJsonAsync<ApiResponse<UploadTextbookResultDto>>();
            Assert.NotNull(firstResult?.Data);
            Assert.NotNull(secondResult?.Data);

            // Lost-response retry (#104): same job, no duplicate work.
            Assert.Equal(firstResult.Data.JobId, secondResult.Data.JobId);
            Assert.Contains("already running", secondResult.Data.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Single(queue.Enqueued.Skip(enqueuedBefore));
        }
        finally
        {
            foreach (var (jobId, filePath) in queue.Enqueued.Skip(enqueuedBefore))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task Upload_SameContentFailedJob_CreatesFreshJob()
    {
        var cookie = await LoginAsContentAdminAsync();
        var queue = _factory.Services.GetRequiredService<RecordingTextbookIngestionQueue>();
        var enqueuedBefore = queue.Enqueued.Count;
        var content = $"failed job re-upload {Guid.NewGuid():N}";

        try
        {
            var first = await UploadAsync(cookie, content);
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            var firstResult = await first.Content.ReadFromJsonAsync<ApiResponse<UploadTextbookResultDto>>();
            Assert.NotNull(firstResult?.Data);
            var firstJobId = firstResult.Data.JobId;

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var job = await db.TextbookJobs.SingleAsync(j => j.Id == firstJobId);
                job.Status = TextbookJobStatus.Failed;
                await db.SaveChangesAsync();
            }

            var second = await UploadAsync(cookie, content);
            Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
            var secondResult = await second.Content.ReadFromJsonAsync<ApiResponse<UploadTextbookResultDto>>();
            Assert.NotNull(secondResult?.Data);

            // Failed work never re-runs (#70): the fresh upload gets a new job.
            Assert.NotEqual(firstJobId, secondResult.Data.JobId);
            Assert.Contains("Background job started", secondResult.Data.Message);
            Assert.Equal(2, queue.Enqueued.Skip(enqueuedBefore).Count());
        }
        finally
        {
            foreach (var (jobId, filePath) in queue.Enqueued.Skip(enqueuedBefore))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task Upload_DisallowedExtension_Returns400()
    {
        var cookie = await LoginAsContentAdminAsync();

        var response = await UploadAsync(cookie, "integration test content", "malware.exe");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.NotNull(result.Errors);
        Assert.Contains("FileName", result.Errors.Keys);
    }

    [Fact]
    public async Task Status_ContentAdmin_UnknownJob_Returns404()
    {
        var cookie = await LoginAsContentAdminAsync();

        var message = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/admin/textbook/status/{Guid.NewGuid()}");
        message.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.NotNull(result);
        Assert.False(result.Success);
    }
}
