using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.MockInterviews;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;
using Xunit;

namespace VoxMentor.Tests.Integration;

/// <summary>
/// Integration tests for POST /api/v1/mock/start and /mock/{id}/answer (#124):
/// daily limit → 429, foreign interview → 404, transcript growth with order
/// preserved, JD-optional start, plus the 400/409 errors from docs/API.md §6.
/// </summary>
/// <remarks>
/// The shared test HttpClient keeps the access_token cookie from the most recent
/// login and wins over any manual Cookie header, so each test logs in as the
/// acting user immediately before that user's requests. Cross-user tests must
/// therefore do the owner's work first, then log in as the intruder.
/// </remarks>
public class MockInterviewApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public MockInterviewApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>Registers a fresh student, logs in (storing the token cookie), and returns their email.</summary>
    private async Task<string> LoginAsStudentAsync()
    {
        var email = $"mock-api-{Guid.NewGuid():N}@example.com";
        var password = "Password@123";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Mock Interview Student",
            email,
            password
        });
        Assert.True(register.IsSuccessStatusCode, $"register failed: {(int)register.StatusCode}");

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        return email;
    }

    /// <summary>POSTs the given JSON body as the most recently logged-in student.</summary>
    private Task<HttpResponseMessage> PostAsync(string url, object body)
        => _client.PostAsJsonAsync(url, body);

    /// <summary>Starts an interview and returns its id; fails the test on any status but 202.</summary>
    private async Task<Guid> StartAsync()
    {
        var response = await PostAsync("/api/v1/mock/start", new { });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<StartMockInterviewResultDto>>();
        Assert.NotNull(result?.Data);
        return result!.Data!.InterviewId;
    }

    /// <summary>Seeds one concept + question so the adaptive selector can answer 200.</summary>
    private async Task SeedConceptWithQuestionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var concept = new Concept
        {
            Id = Guid.NewGuid(),
            Name = $"Mock Concept {Guid.NewGuid():N}",
            Description = "desc",
            DifficultyLevel = 2,
            Category = "Test"
        };
        db.Concepts.Add(concept);
        db.Questions.Add(new Question
        {
            Id = Guid.NewGuid(),
            ConceptId = concept.Id,
            Title = "Mock Question",
            Description = "Desc",
            QuestionType = "Code",
            Difficulty = 3,
            TestCases = new[] { "{\"input\":\"1\",\"expected\":\"1\"}" },
            HiddenTestCaseCount = 0
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds an interview owned by the user with the given email and status.</summary>
    private async Task<Guid> SeedInterviewForUserAsync(string email, MockInterviewStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == email);
        var interview = new MockInterview
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Type = InterviewType.Technical,
            TotalQuestions = 3,
            Status = status,
            StartedAt = DateTime.UtcNow,
            TranscriptJson = "[]"
        };
        db.MockInterviews.Add(interview);
        await db.SaveChangesAsync();
        return interview.Id;
    }

    /// <summary>Reads the persisted transcript cross-scope (tests run outside any request).</summary>
    private async Task<List<TranscriptTurn>> ReadTranscriptAsync(Guid interviewId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var interview = await db.MockInterviews.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(m => m.Id == interviewId);
        return JsonSerializer.Deserialize<List<TranscriptTurn>>(
            interview.TranscriptJson!, TranscriptTurn.JsonOptions)!;
    }

    [Fact]
    public async Task Start_WithoutJd_Returns202_WithOpeningMessage()
    {
        await LoginAsStudentAsync();
        await SeedConceptWithQuestionAsync();

        var response = await PostAsync("/api/v1/mock/start", new { });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<StartMockInterviewResultDto>>();
        Assert.NotNull(result?.Data);
        Assert.Equal("InProgress", result!.Data!.Status);
        Assert.Equal("Technical", result.Data.Type);
        Assert.Equal(StubMockInterviewGenerator.Opening, result.Data.OpeningMessage);
    }

    [Fact]
    public async Task Start_UnknownJd_Returns404()
    {
        await LoginAsStudentAsync();

        var response = await PostAsync("/api/v1/mock/start", new { jdId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Start_SecondStartSameDay_Returns429_WithRetryAfter()
    {
        await LoginAsStudentAsync();
        await SeedConceptWithQuestionAsync();
        await StartAsync();

        var response = await PostAsync("/api/v1/mock/start", new { });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"), "429 must carry Retry-After");
    }

    [Fact]
    public async Task Answer_OtherStudentsInterview_Returns404()
    {
        // Owner's work must happen while the client still carries the owner's
        // token; the intruder login below replaces it for the final request.
        await LoginAsStudentAsync();
        await SeedConceptWithQuestionAsync();
        var ownerInterview = await StartAsync();

        await LoginAsStudentAsync();
        var response = await PostAsync($"/api/v1/mock/{ownerInterview}/answer",
            new { message = "not my interview" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Answer_EndedInterview_Returns409()
    {
        var email = await LoginAsStudentAsync();
        var endedId = await SeedInterviewForUserAsync(email, MockInterviewStatus.Completed);

        var response = await PostAsync($"/api/v1/mock/{endedId}/answer", new { message = "hello" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Answer_EmptyMessage_Returns400()
    {
        await LoginAsStudentAsync();
        await SeedConceptWithQuestionAsync();
        var interviewId = await StartAsync();

        var response = await PostAsync($"/api/v1/mock/{interviewId}/answer", new { message = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Answer_GrowsTranscriptByTwoTurnsPerAnswer_PreservesOrder()
    {
        await LoginAsStudentAsync();
        await SeedConceptWithQuestionAsync();
        var interviewId = await StartAsync();

        for (var i = 1; i <= 2; i++)
        {
            var response = await PostAsync($"/api/v1/mock/{interviewId}/answer",
                new { message = $"answer {i}" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<AnswerMockInterviewResultDto>>();
            Assert.NotNull(result?.Data);
            Assert.Equal(2 * i + 1, result!.Data!.TurnNumber);
            Assert.Equal(StubMockInterviewGenerator.FollowUp, result.Data.InterviewerMessage);
            Assert.True(result.Data.InterviewOngoing);
        }

        var turns = await ReadTranscriptAsync(interviewId);
        Assert.Equal(5, turns.Count);
        Assert.Equal(new[]
        {
            TranscriptTurn.InterviewerRole,
            TranscriptTurn.CandidateRole,
            TranscriptTurn.InterviewerRole,
            TranscriptTurn.CandidateRole,
            TranscriptTurn.InterviewerRole
        }, turns.Select(t => t.Role).ToArray());
        Assert.Equal(new[] { "answer 1", "answer 2" },
            turns.Where(t => t.Role == TranscriptTurn.CandidateRole).Select(t => t.Message).ToArray());
        Assert.Equal(StubMockInterviewGenerator.Opening, turns[0].Message);
    }
}
