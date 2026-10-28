using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.MockInterviews;
using VoxMentor.Application.Features.Practice.GetReadiness;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;
using Xunit;

namespace VoxMentor.Tests.Integration;

/// <summary>
/// Integration tests for the mock-interview REST surface: start/answer (#124)
/// and complete/review/history (#125) — daily limit → 429, foreign interview →
/// 404, transcript growth, review-before-complete → 404, history scoping and
/// score nullability, plus the 400/409 errors from docs/API.md §6.
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
    private async Task<Guid> SeedInterviewForUserAsync(
        string email, MockInterviewStatus status, DateTime? startedAt = null)
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
            StartedAt = startedAt ?? DateTime.UtcNow,
            TranscriptJson = "[]"
        };
        db.MockInterviews.Add(interview);
        await db.SaveChangesAsync();
        return interview.Id;
    }

    /// <summary>
    /// Seeds JD + weight + concept + question + 0.5 mastery for one unique
    /// skill name, so the student's readiness is exactly 50 (0.5 × 1 × 100).
    /// Returns the jdId for scoped readiness calls.
    /// </summary>
    private async Task<Guid> SeedReadinessScenarioAsync(string email, string skillName)
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
        db.JdSkillWeights.Add(new JdSkillWeight
        {
            Id = Guid.NewGuid(),
            JobDescriptionId = jd.Id,
            SkillName = skillName,
            Weight = 1f,
            IsTechnical = true
        });

        var concept = new Concept
        {
            Id = Guid.NewGuid(),
            Name = skillName,
            Description = "desc",
            DifficultyLevel = 2,
            Category = "Test"
        };
        db.Concepts.Add(concept);
        db.Questions.Add(new Question
        {
            Id = Guid.NewGuid(),
            ConceptId = concept.Id,
            Title = "Weighted Question",
            Description = "Desc",
            QuestionType = "Code",
            Difficulty = 3,
            TestCases = new[] { "{\"input\":\"1\",\"expected\":\"1\"}" },
            HiddenTestCaseCount = 0
        });
        db.StudentMasteries.Add(new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ConceptId = concept.Id,
            MasteryProbability = 0.5f
        });
        await db.SaveChangesAsync();
        return jd.Id;
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

    [Fact]
    public async Task Complete_ThenReview_Returns200_PersistsScoreStatusCompletedAt()
    {
        await LoginAsStudentAsync();
        await SeedConceptWithQuestionAsync();
        var interviewId = await StartAsync();

        var complete = await PostAsync($"/api/v1/mock/{interviewId}/complete", new { });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var review = await complete.Content.ReadFromJsonAsync<ApiResponse<MockInterviewReview>>();
        Assert.NotNull(review?.Data);
        Assert.Equal(StubMockInterviewGenerator.ReviewScore, review!.Data!.Score);
        Assert.Equal("Technical", review.Data.Type);
        Assert.Equal("problem1", Assert.Single(review.Data.TechnicalReview).Key);
        Assert.Equal(StubMockInterviewGenerator.ReviewNextSteps, review.Data.NextSteps);

        // Done-when #1: score persisted, status flips, CompletedAt set.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.MockInterviews.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(m => m.Id == interviewId);
            Assert.Equal(MockInterviewStatus.Completed, stored.Status);
            Assert.NotNull(stored.CompletedAt);
            Assert.Equal(StubMockInterviewGenerator.ReviewScore, stored.Score);
            Assert.False(string.IsNullOrEmpty(stored.ReviewJson));
        }

        var fetch = await _client.GetAsync($"/api/v1/mock/{interviewId}/review");
        Assert.Equal(HttpStatusCode.OK, fetch.StatusCode);
        var result = await fetch.Content.ReadFromJsonAsync<ApiResponse<GetMockInterviewReviewResultDto>>();
        Assert.NotNull(result?.Data);
        Assert.Equal(interviewId, result!.Data!.InterviewId);
        Assert.Equal("Completed", result.Data.Status);
        Assert.Equal(StubMockInterviewGenerator.ReviewScore, result.Data.Score);
        Assert.Equal(review.Data.NextSteps, result.Data.Review.NextSteps);
        Assert.Equal(review.Data.ReadinessDelta, result.Data.Review.ReadinessDelta);
        Assert.NotNull(result.Data.CompletedAt);
    }

    [Fact]
    public async Task Review_BeforeComplete_Returns404()
    {
        await LoginAsStudentAsync();
        await SeedConceptWithQuestionAsync();
        var interviewId = await StartAsync();

        var response = await _client.GetAsync($"/api/v1/mock/{interviewId}/review");

        // Done-when #2: an uncompleted interview has no review to fetch.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Complete_AlreadyCompleted_Returns409()
    {
        var email = await LoginAsStudentAsync();
        var endedId = await SeedInterviewForUserAsync(email, MockInterviewStatus.Completed);

        var response = await PostAsync($"/api/v1/mock/{endedId}/complete", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task History_ReturnsOwnInterviewsNewestFirst_ScoreNullUntilCompleted()
    {
        var email = await LoginAsStudentAsync();
        await SeedConceptWithQuestionAsync();

        // Yesterday, so the row stays out of today's start quota (limit = 1).
        var pastId = await SeedInterviewForUserAsync(
            email, MockInterviewStatus.InProgress, startedAt: DateTime.UtcNow.AddDays(-1));
        var completedId = await StartAsync();
        var complete = await PostAsync($"/api/v1/mock/{completedId}/complete", new { });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var response = await _client.GetAsync("/api/v1/mock/history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<ApiResponse<GetMockInterviewHistoryResultDto>>();
        Assert.NotNull(page?.Data);

        // Newest first; score only once Completed.
        var items = page!.Data!.Interviews;
        Assert.Equal(2, items.Count);
        Assert.Equal(completedId, items[0].InterviewId);
        Assert.Equal("Completed", items[0].Status);
        Assert.Equal(StubMockInterviewGenerator.ReviewScore, items[0].Score);
        Assert.Equal(pastId, items[1].InterviewId);
        Assert.Equal("InProgress", items[1].Status);
        Assert.Null(items[1].Score);

        // Done-when #3: a second student's history never shows these rows.
        var intruderEmail = await LoginAsStudentAsync();
        var intruderId = await SeedInterviewForUserAsync(intruderEmail, MockInterviewStatus.InProgress);
        var intruderResponse = await _client.GetAsync("/api/v1/mock/history");
        var intruderPage = await intruderResponse.Content
            .ReadFromJsonAsync<ApiResponse<GetMockInterviewHistoryResultDto>>();
        Assert.NotNull(intruderPage?.Data);
        var intruderItems = intruderPage!.Data!.Interviews;
        Assert.Single(intruderItems);
        Assert.Equal(intruderId, intruderItems[0].InterviewId);
        Assert.DoesNotContain(intruderItems, i => i.InterviewId == completedId);
        Assert.DoesNotContain(intruderItems, i => i.InterviewId == pastId);
    }

    [Fact]
    public async Task Complete_ReadinessDeltaBefore_MatchesPriorReadinessScore()
    {
        var email = await LoginAsStudentAsync();
        var skillName = $"Skill{Guid.NewGuid():N}";
        var jdId = await SeedReadinessScenarioAsync(email, skillName);

        var prior = await _client.GetAsync($"/api/v1/student/readiness?jdId={jdId}");
        Assert.Equal(HttpStatusCode.OK, prior.StatusCode);
        var priorData = await prior.Content.ReadFromJsonAsync<ApiResponse<ReadinessDto>>();
        Assert.NotNull(priorData?.Data);
        Assert.Equal(50f, priorData!.Data!.Score);

        var interviewId = await StartAsync();
        var complete = await PostAsync($"/api/v1/mock/{interviewId}/complete", new { });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var review = await complete.Content.ReadFromJsonAsync<ApiResponse<MockInterviewReview>>();
        Assert.NotNull(review?.Data);

        // Done-when #4: before == the score captured prior to the review running.
        Assert.Equal(priorData.Data.Score, review!.Data!.ReadinessDelta.Before);
    }
}
