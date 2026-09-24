using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Practice.GetQuestions;
using VoxMentor.Domain.Entities;
using VoxMentor.Infrastructure.Persistence;
using Xunit;

namespace VoxMentor.Tests.Integration;

/// <summary>
/// Integration tests for GET /api/v1/questions, covering the class-based
/// [FromQuery] binding introduced by issue #81: flat query keys, defaults,
/// and filters through the real HTTP pipeline.
/// </summary>
public class QuestionsApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public QuestionsApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> LoginAsStudentAsync()
    {
        var email = $"student-questions-{Guid.NewGuid():N}@example.com";
        const string password = "Password@123";

        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Questions Api Student",
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

    private async Task<(Concept Concept, List<Question> Questions)> SeedAsync(int difficulty)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var concept = new Concept
        {
            Id = Guid.NewGuid(),
            Name = $"Binding Concept {Guid.NewGuid():N}",
            Description = "desc",
            DifficultyLevel = 2,
            Category = "Data Structures"
        };
        db.Concepts.Add(concept);

        for (var i = 0; i < 3; i++)
        {
            db.Questions.Add(new Question
            {
                Id = Guid.NewGuid(),
                ConceptId = concept.Id,
                Title = $"Q{i + 1}",
                Description = "Desc",
                QuestionType = "Code",
                Difficulty = difficulty,
                TestCases = new[] { "{\"input\":\"0\",\"expected\":\"0\"}" },
                HiddenTestCaseCount = 0
            });
        }

        await db.SaveChangesAsync();
        return (concept, db.Questions.Where(q => q.ConceptId == concept.Id).ToList());
    }

    [Fact]
    public async Task GetQuestions_NoQueryString_UsesDefaultPaging()
    {
        var cookie = await LoginAsStudentAsync();

        var message = new HttpRequestMessage(HttpMethod.Get, "/api/v1/questions");
        message.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<GetQuestionsResultDto>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(20, result.Data.PageSize);
    }

    [Fact]
    public async Task GetQuestions_FlatQueryKeys_BindPagingAndFilters()
    {
        var cookie = await LoginAsStudentAsync();
        var (concept, _) = await SeedAsync(difficulty: 7);

        // Noise rows so each filter is distinguishable: if conceptId or
        // difficulty binding is dropped, TotalCount becomes 4+ and fails.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Questions.Add(new Question
            {
                Id = Guid.NewGuid(),
                ConceptId = concept.Id,
                Title = "WrongDifficulty",
                Description = "Desc",
                QuestionType = "Code",
                Difficulty = 3,
                TestCases = new[] { "{\"input\":\"0\",\"expected\":\"0\"}" },
                HiddenTestCaseCount = 0
            });
            var otherConcept = new Concept
            {
                Id = Guid.NewGuid(),
                Name = $"Other Concept {Guid.NewGuid():N}",
                Description = "desc",
                DifficultyLevel = 2,
                Category = "Data Structures"
            };
            db.Concepts.Add(otherConcept);
            db.Questions.Add(new Question
            {
                Id = Guid.NewGuid(),
                ConceptId = otherConcept.Id,
                Title = "WrongConcept",
                Description = "Desc",
                QuestionType = "Code",
                Difficulty = 7,
                TestCases = new[] { "{\"input\":\"0\",\"expected\":\"0\"}" },
                HiddenTestCaseCount = 0
            });
            await db.SaveChangesAsync();
        }

        var url = $"/api/v1/questions?conceptId={concept.Id}&difficulty=7&page=1&pageSize=1";
        var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<GetQuestionsResultDto>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(1, result.Data.PageSize);
        Assert.Equal(3, result.Data.TotalCount);
        var q = Assert.Single(result.Data.Questions);
        Assert.Equal(concept.Id, q.ConceptId);
        Assert.Equal(7, q.Difficulty);
    }
}
