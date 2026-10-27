using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Auth.Register;
using VoxMentor.Application.Features.MockInterviews;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;
using VoxMentor.Infrastructure.Persistence;
using Xunit;

namespace VoxMentor.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection",
            "Host=localhost;Port=5432;Database=voxmentor-tests;Username=postgres;Password=postgres;Ssl Mode=VerifyFull;");
        // Disable Hangfire storage — would dial localhost:5432 at server start.
        builder.UseSetting("ConnectionStrings:Hangfire", "");
        builder.UseSetting("JwtSettings:Secret", "TestJwtSecretKeyThatIsAtLeast32BytesLong!!");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));

            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            var appDbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IApplicationDbContext));

            if (appDbContextDescriptor != null)
            {
                services.Remove(appDbContextDescriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
            });

            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        });

        // Hangfire is disabled above, so Program registers the fail-fast
        // NullTextbookIngestionQueue. Swap in a recording queue so the upload
        // success path (#104) is testable; runs after the app's registrations.
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<RecordingTextbookIngestionQueue>();
            services.AddSingleton<ITextbookIngestionQueue>(
                provider => provider.GetRequiredService<RecordingTextbookIngestionQueue>());

            // #124: deterministic dialogue instead of dialing Ollama from tests.
            services.AddSingleton<StubMockInterviewGenerator>();
            services.AddSingleton<IMockInterviewGenerator>(
                provider => provider.GetRequiredService<StubMockInterviewGenerator>());
        });
    }
}

/// <summary>
/// Deterministic mock-interview lines (#124): /mock/* tests assert stable text
/// instead of whatever a local Ollama instance would generate.
/// </summary>
public sealed class StubMockInterviewGenerator : IMockInterviewGenerator
{
    /// <summary>Fixed opening line returned for every /mock/start.</summary>
    public const string Opening = "STUB OPENING: here is your first problem.";

    /// <summary>Fixed probe returned for every /mock/{id}/answer.</summary>
    public const string FollowUp = "STUB PROBE: what is the time complexity?";

    /// <summary>Fixed overall score returned for every /mock/{id}/complete.</summary>
    public const float ReviewScore = 77f;

    /// <summary>Fixed next-steps line returned for every /mock/{id}/complete.</summary>
    public const string ReviewNextSteps = "STUB NEXT STEPS: focus on complexity analysis.";

    public async IAsyncEnumerable<TutorChunk> OpeningStreamAsync(
        string? company, string? role, string problemTitle,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return new TutorChunk(Opening);
    }

    public async IAsyncEnumerable<TutorChunk> FollowUpStreamAsync(
        IReadOnlyList<TranscriptTurn> transcript,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return new TutorChunk(FollowUp);
    }

    public Task<MockInterviewReviewDraft?> ReviewAsync(
        IReadOnlyList<TranscriptTurn> transcript,
        InterviewType type,
        string? company,
        string? role,
        CancellationToken cancellationToken = default)
        => Task.FromResult<MockInterviewReviewDraft?>(new MockInterviewReviewDraft(
            ReviewScore,
            new Dictionary<string, ProblemReview>(StringComparer.Ordinal)
            {
                ["problem1"] = new ProblemReview(
                    Solved: true,
                    Complexity: "Discussed",
                    Feedback: "STUB REVIEW: solid approach.")
            },
            ReviewNextSteps));
}

/// <summary>
/// Records enqueues and scheduled recovery runs instead of dialing Hangfire,
/// so upload success paths are testable with the storage-free factory above.
/// </summary>
public sealed class RecordingTextbookIngestionQueue : ITextbookIngestionQueue
{
    private readonly ConcurrentQueue<(Guid JobId, string FilePath)> _enqueued = new();
    private readonly ConcurrentQueue<(Guid JobId, string FilePath, TimeSpan Delay)> _scheduled = new();

    public IReadOnlyCollection<(Guid JobId, string FilePath)> Enqueued => _enqueued.ToArray();

    public IReadOnlyCollection<(Guid JobId, string FilePath, TimeSpan Delay)> Scheduled
        => _scheduled.ToArray();

    public void Enqueue(Guid jobId, string filePath) => _enqueued.Enqueue((jobId, filePath));

    public void Schedule(Guid jobId, string filePath, TimeSpan delay)
        => _scheduled.Enqueue((jobId, filePath, delay));
}

public class RegisterApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public RegisterApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_ValidStudent_Returns201Created_AndAssignsStudentRole()
    {
        var request = new
        {
            fullName = "Student User",
            email = "student1@example.com",
            password = "Password@123"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<RegisterResponseDto>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("Student User", result.Data.FullName);
        Assert.Equal("student1@example.com", result.Data.Email);
        Assert.Equal("Student", result.Data.Role);

        // Confirm persisted in DB
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == "student1@example.com");
        Assert.NotNull(user);
        Assert.Equal("Student User", user.FullName);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409Conflict()
    {
        var request = new
        {
            fullName = "Duplicate User",
            email = "dupuser@example.com",
            password = "Password@123"
        };

        var firstResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var result = await secondResponse.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("already registered", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_MissingFullName_Returns400BadRequest()
    {
        var request = new
        {
            fullName = "",
            email = "nofullname@example.com",
            password = "Password@123"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidEmail_Returns400BadRequest()
    {
        var request = new
        {
            fullName = "Test User",
            email = "not-an-email",
            password = "Password@123"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WeakPassword_Returns400BadRequest()
    {
        var request = new
        {
            fullName = "Test User",
            email = "weakpass@example.com",
            password = "weak"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_AttemptToPassAdminRole_MustNotCreateAdminRole()
    {
        var request = new
        {
            fullName = "Sneaky Admin Attempt",
            email = "sneakyadmin@example.com",
            password = "Password@123",
            role = "Admin"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<RegisterResponseDto>>();
        Assert.NotNull(result);
        Assert.NotNull(result.Data);
        Assert.Equal("Student", result.Data.Role);
    }
}
