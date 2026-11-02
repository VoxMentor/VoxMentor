using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Resume.AnalyzeResume;
using VoxMentor.Domain.Entities;
using VoxMentor.Infrastructure.Persistence;
using Xunit;

namespace VoxMentor.Tests.Integration;

/// <summary>
/// #151 resume ATS analyzer endpoint: authz, validation edges, JD lookup, happy path.
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

        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var accessTokenCookie = cookies.FirstOrDefault(c => c.StartsWith("access_token="));
        Assert.NotNull(accessTokenCookie);
        return accessTokenCookie.Split(';')[0];
    }

    private async Task<Guid> SeedJdAsync(string email, params (string skill, float weight)[] skills)
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
            EstimatedWeeks = 6
        };
        db.JobDescriptions.Add(jd);
        foreach (var (skill, weight) in skills)
        {
            db.JdSkillWeights.Add(new JdSkillWeight
            {
                Id = Guid.NewGuid(),
                JobDescriptionId = jd.Id,
                SkillName = skill,
                Weight = weight,
                IsTechnical = true
            });
        }
        await db.SaveChangesAsync();
        return jd.Id;
    }

    private static MultipartFormDataContent UploadBody(byte[] bytes, string fileName, Guid jdId)
    {
        var body = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        body.Add(fileContent, "file", fileName);
        body.Add(new StringContent(jdId.ToString()), "jdId");
        return body;
    }

    private static byte[] MinimalPdf(string text)
    {
        // Xref offsets use char counts, so the builder is ASCII-only.
        Assert.True(text.All(c => c < 128), "MinimalPdf builder only supports ASCII text.");
        // Build a minimal single-page PDF with correct xref offsets.
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

        var sb = new System.Text.StringBuilder();
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
        return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Fact]
    public async Task Analyze_Anonymous_Returns401()
    {
        var response = await _client.PostAsync("/api/v1/resume/analyze",
            UploadBody(MinimalPdf("hello"), "resume.pdf", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_MissingFile_Returns400()
    {
        var email = $"resume-{Guid.NewGuid():N}@example.com";
        var cookie = await LoginAsStudentAsync(email);
        var jdId = await SeedJdAsync(email, ("Python", 1f));
        var body = new MultipartFormDataContent();
        body.Add(new StringContent(jdId.ToString()), "jdId");
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/resume/analyze") { Content = body };
        msg.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(msg);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_WrongExtension_Returns400()
    {
        var email = $"resume-{Guid.NewGuid():N}@example.com";
        var cookie = await LoginAsStudentAsync(email);
        var jdId = await SeedJdAsync(email, ("Python", 1f));
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/resume/analyze")
        {
            Content = UploadBody(MinimalPdf("hi"), "resume.exe", jdId)
        };
        msg.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(msg);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_EmptyFile_Returns400()
    {
        var email = $"resume-{Guid.NewGuid():N}@example.com";
        var cookie = await LoginAsStudentAsync(email);
        var jdId = await SeedJdAsync(email, ("Python", 1f));
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/resume/analyze")
        {
            Content = UploadBody(Array.Empty<byte>(), "resume.pdf", jdId)
        };
        msg.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(msg);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_CorruptPdf_Returns400()
    {
        var email = $"resume-{Guid.NewGuid():N}@example.com";
        var cookie = await LoginAsStudentAsync(email);
        var jdId = await SeedJdAsync(email, ("Python", 1f));
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/resume/analyze")
        {
            Content = UploadBody(new byte[] { 1, 2, 3, 4, 5 }, "resume.pdf", jdId)
        };
        msg.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(msg);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_UnknownJd_Returns404()
    {
        var email = $"resume-{Guid.NewGuid():N}@example.com";
        var cookie = await LoginAsStudentAsync(email);
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/resume/analyze")
        {
            Content = UploadBody(MinimalPdf("Python"), "resume.pdf", Guid.NewGuid())
        };
        msg.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(msg);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_ForeignJd_Returns404()
    {
        var emailA = $"resumea-{Guid.NewGuid():N}@example.com";
        var emailB = $"resumeb-{Guid.NewGuid():N}@example.com";
        var cookieA = await LoginAsStudentAsync(emailA);
        await LoginAsStudentAsync(emailB);
        var foreignJdId = await SeedJdAsync(emailB, ("Python", 1f));
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/resume/analyze")
        {
            Content = UploadBody(MinimalPdf("Python"), "resume.pdf", foreignJdId)
        };
        msg.Headers.Add("Cookie", cookieA);
        var response = await _client.SendAsync(msg);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_HappyPath_MatchesSkillsAndReportsGaps()
    {
        var email = $"resume-{Guid.NewGuid():N}@example.com";
        var cookie = await LoginAsStudentAsync(email);
        var jdId = await SeedJdAsync(email, ("Python", 0.5f), ("SQL", 0.3f), ("Docker", 0.2f));

        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/resume/analyze")
        {
            Content = UploadBody(MinimalPdf("Experienced Python and SQL developer"), "resume.pdf", jdId)
        };
        msg.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(msg);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AnalyzeResumeResultDto>>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        var data = body.Data!;
        Assert.Equal(80, data.MatchScore); // (0.5+0.3)/1.0
        Assert.Contains("Python", data.KeywordMatch.Matched);
        Assert.Contains("SQL", data.KeywordMatch.Matched);
        Assert.Contains("Docker", data.KeywordMatch.Missing);
        Assert.DoesNotContain("Python", data.KeywordMatch.Missing);
        Assert.True(data.AtsParseability.Score < 100); // missing sections
        Assert.Contains(data.Suggestions, s => s.Contains("Docker"));
    }

    [Fact]
    public async Task Analyze_TxtFile_Works()
    {
        var email = $"resume-{Guid.NewGuid():N}@example.com";
        var cookie = await LoginAsStudentAsync(email);
        var jdId = await SeedJdAsync(email, ("Python", 1f));

        var body = new MultipartFormDataContent();
        body.Add(new StringContent("python developer", System.Text.Encoding.UTF8, "text/plain"), "file", "resume.txt");
        body.Add(new StringContent(jdId.ToString()), "jdId");
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/resume/analyze") { Content = body };
        msg.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(msg);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_BinaryTxt_Returns400()
    {
        var email = $"resume-{Guid.NewGuid():N}@example.com";
        var cookie = await LoginAsStudentAsync(email);
        var jdId = await SeedJdAsync(email, ("Python", 1f));

        var bytes = new byte[] { 0xFF, 0x00, 0x01, 0x02, 0x03, 0x80, 0x90 };
        var body = new MultipartFormDataContent();
        body.Add(new ByteArrayContent(bytes), "file", "resume.txt");
        body.Add(new StringContent(jdId.ToString()), "jdId");
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/resume/analyze") { Content = body };
        msg.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(msg);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
