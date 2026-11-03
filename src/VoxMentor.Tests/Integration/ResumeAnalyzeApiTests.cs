using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;
using VoxMentor.Infrastructure.Persistence;
using Xunit;

namespace VoxMentor.Tests.Integration;

/// <summary>
/// #151 POST /api/v1/resume/analyze: authz, validation, not-found, and the
/// happy-path PDF round-trip against seeded JD skill weights.
/// </summary>
public class ResumeAnalyzeApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public ResumeAnalyzeApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    private static byte[] MinimalPdf(string text)
    {
        var escaped = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var content = $"BT /F1 12 Tf 72 720 Td ({escaped}) Tj ET";
        var objects = new List<string>
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n",
            $"4 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n",
            "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n",
        };
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        var offsets = new List<int>();
        foreach (var obj in objects)
        {
            offsets.Add(sb.Length);
            sb.Append(obj);
        }
        var xrefPos = sb.Length;
        sb.Append("xref\n");
        sb.Append($"0 {objects.Count + 1}\n");
        sb.Append("0000000000 65535 f \n");
        foreach (var off in offsets)
            sb.Append($"{off:D10} 00000 n \n");
        sb.Append("trailer\n");
        sb.Append($"<< /Size {objects.Count + 1} /Root 1 0 R >>\n");
        sb.Append("startxref\n");
        sb.Append($"{xrefPos}\n");
        sb.Append("%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private async Task<string> LoginAsStudentAsync(string email)
    {
        var password = "Password@123";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Resume Api Student",
            email,
            password
        });
        Assert.True(register.IsSuccessStatusCode, $"register failed: {(int)register.StatusCode}");

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var accessTokenCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.StartsWith("access_token="));
        Assert.NotNull(accessTokenCookie);
        return accessTokenCookie.Split(';')[0];
    }

    /// <summary>Seeds a JD + weighted skills owned by the given email; returns its id.</summary>
    private async Task<Guid> SeedJdWithSkillsAsync(string email, params (string skill, float weight, bool technical)[] skills)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == email);
        var jd = new JobDescription
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CompanyName = "Acme",
            Role = "SDE-1",
            RawText = "Acme is hiring...",
            Difficulty = "Medium",
            EstimatedWeeks = 4,
            CreatedAt = DateTime.UtcNow
        };
        db.JobDescriptions.Add(jd);
        foreach (var (skill, weight, technical) in skills)
        {
            db.JdSkillWeights.Add(new JdSkillWeight
            {
                Id = Guid.NewGuid(),
                JobDescriptionId = jd.Id,
                SkillName = skill,
                Weight = weight,
                IsTechnical = technical
            });
        }
        await db.SaveChangesAsync();
        return jd.Id;
    }

    private static MultipartFormDataContent UploadBody(byte[] fileBytes, string fileName, Guid jdId)
    {
        var body = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        body.Add(fileContent, "file", fileName);
        body.Add(new StringContent(jdId.ToString()), "jdId");
        return body;
    }

    private async Task<HttpResponseMessage> AnalyzeAsync(string cookie, byte[] fileBytes, string fileName, Guid jdId)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/resume/analyze")
        {
            Content = UploadBody(fileBytes, fileName, jdId)
        };
        message.Headers.Add("Cookie", cookie);
        return await _client.SendAsync(message);
    }

    [Fact]
    public async Task Analyze_Anonymous_Returns401()
    {
        var response = await _client.PostAsync("/api/v1/resume/analyze", UploadBody(MinimalPdf("x"), "resume.pdf", Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_MissingFile_Returns400()
    {
        var cookie = await LoginAsStudentAsync($"student-missing-{Guid.NewGuid():N}@example.com");
        var body = new MultipartFormDataContent();
        body.Add(new StringContent(Guid.NewGuid().ToString()), "jdId");
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/resume/analyze") { Content = body };
        message.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_WrongExtension_Returns400()
    {
        var cookie = await LoginAsStudentAsync($"student-ext-{Guid.NewGuid():N}@example.com");
        var response = await AnalyzeAsync(cookie, Encoding.UTF8.GetBytes("not pdf"), "resume.txt", Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_EmptyFile_Returns400()
    {
        var cookie = await LoginAsStudentAsync($"student-empty-{Guid.NewGuid():N}@example.com");
        var response = await AnalyzeAsync(cookie, Array.Empty<byte>(), "resume.pdf", Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_CorruptPdf_Returns400()
    {
        var email = $"student-corrupt-{Guid.NewGuid():N}@example.com";
        var cookie = await LoginAsStudentAsync(email);
        var jdId = await SeedJdWithSkillsAsync(email, ("Python", 1.0f, true));
        var response = await AnalyzeAsync(cookie, new byte[] { 1, 2, 3, 4, 5 }, "resume.pdf", jdId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_UnknownJdId_Returns404()
    {
        var cookie = await LoginAsStudentAsync($"student-ujd-{Guid.NewGuid():N}@example.com");
        var response = await AnalyzeAsync(cookie, MinimalPdf("Python developer"), "resume.pdf", Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_ForeignJdId_Returns404()
    {
        var ownerEmail = $"student-owner-{Guid.NewGuid():N}@example.com";
        await LoginAsStudentAsync(ownerEmail); // register the owner so the seed resolves a user row
        var foreignJd = await SeedJdWithSkillsAsync(ownerEmail, ("Python", 1.0f, true));

        var cookie = await LoginAsStudentAsync($"student-foreign-{Guid.NewGuid():N}@example.com");
        var response = await AnalyzeAsync(cookie, MinimalPdf("Python developer"), "resume.pdf", foreignJd);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_HappyPath_ReturnsFitReport()
    {
        var email = $"student-happy-{Guid.NewGuid():N}@example.com";
        var cookie = await LoginAsStudentAsync(email);
        var jdId = await SeedJdWithSkillsAsync(
            email,
            ("Python", 0.5f, true),
            ("Databases", 0.3f, true),
            ("Teamwork", 0.2f, false));

        // Resume mentions Python + Databases + teamwork → 100% match.
        var response = await AnalyzeAsync(
            cookie, MinimalPdf("Python developer with Databases experience and strong teamwork."),
            "resume.pdf", jdId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AnalyzeResultDto>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(100, result.Data!.MatchScore);
        Assert.Equal(3, result.Data.KeywordMatch.Matched.Count);
        Assert.Empty(result.Data.KeywordMatch.Missing);
        Assert.InRange(result.Data.AtsParseability.Score, 0, 100);
    }

    [Fact]
    public async Task Analyze_PartialMatch_ReturnsMissingSkills()
    {
        var email = $"student-partial-{Guid.NewGuid():N}@example.com";
        var cookie = await LoginAsStudentAsync(email);
        var jdId = await SeedJdWithSkillsAsync(
            email,
            ("Python", 0.6f, true),
            ("Databases", 0.4f, true));

        var response = await AnalyzeAsync(
            cookie, MinimalPdf("Python developer."), "resume.pdf", jdId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AnalyzeResultDto>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(60, result.Data!.MatchScore); // 0.6 / 1.0
        Assert.Single(result.Data.KeywordMatch.Matched);
        Assert.Single(result.Data.KeywordMatch.Missing);
    }

    [Fact]
    public async Task Analyze_NonStudent_Returns403()
    {
        var email = $"nonstudent-{Guid.NewGuid():N}@example.com";
        var password = "Password@123";

        // Create a user directly without the Student role (registration always
        // assigns Student, so we bypass it to get an authenticated non-student).
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Non Student" };
        var createResult = await userManager.CreateAsync(user, password);
        Assert.True(createResult.Succeeded);

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var accessTokenCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.StartsWith("access_token="));
        Assert.NotNull(accessTokenCookie);
        var cookie = accessTokenCookie.Split(';')[0];

        var jdId = await SeedJdWithSkillsAsync(email, ("Python", 1.0f, true));
        var response = await AnalyzeAsync(cookie, MinimalPdf("Python developer"), "resume.pdf", jdId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Rate-limit behavior is exercised by the Redis-backed IRateLimiter in
    // production (see AnalyzeResumeHandler). It cannot be unit-tested here
    // because Redis is not available in the test environment.

    private sealed class AnalyzeResultDto
    {
        public int MatchScore { get; set; }
        public KeywordMatchDto KeywordMatch { get; set; } = new();
        public AtsParseabilityDto AtsParseability { get; set; } = new();
        public List<string> Suggestions { get; set; } = new();
    }

    private sealed class KeywordMatchDto
    {
        public List<string> Matched { get; set; } = new();
        public List<string> Missing { get; set; } = new();
    }

    private sealed class AtsParseabilityDto
    {
        public int Score { get; set; }
        public List<string> Issues { get; set; } = new();
    }
}
