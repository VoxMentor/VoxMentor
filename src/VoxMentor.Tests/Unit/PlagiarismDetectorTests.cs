using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Infrastructure.Persistence;
using VoxMentor.Infrastructure.Plagiarism;

namespace VoxMentor.Tests.Unit;

public class PlagiarismDetectorTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        var a = new float[] { 1, 0, 0, 1 };
        var b = new float[] { 1, 0, 0, 1 };

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.Equal(1f, result, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var a = new float[] { 1, 0 };
        var b = new float[] { 0, 1 };

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.Equal(0f, result, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        var a = new float[] { 1, 0 };
        var b = new float[] { -1, 0 };

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.Equal(-1f, result, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_SimilarVectors_ReturnsHighScore()
    {
        var a = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
        var b = new float[] { 0.11f, 0.21f, 0.29f, 0.41f, 0.49f };

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.True(result > 0.99f, $"Expected > 0.99 but got {result}");
    }

    [Fact]
    public void CosineSimilarity_DifferentVectors_ReturnsLowerScore()
    {
        var a = new float[] { 1, 0, 0 };
        var b = new float[] { 0, 1, 0 };

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.Equal(0f, result, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_DimensionMismatch_ThrowsArgumentException()
    {
        var a = new float[] { 1, 0 };
        var b = new float[] { 1, 0, 0 };

        Assert.Throws<ArgumentException>(() => PlagiarismDetector.CosineSimilarity(a, b));
    }

    [Fact]
    public void CosineSimilarity_AllZeroVectors_ReturnsZero()
    {
        var a = new float[] { 0, 0, 0 };
        var b = new float[] { 0, 0, 0 };

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.Equal(0f, result, precision: 4);
    }

    [Fact]
    public void CosineSimilarity_ScaledVectors_ReturnsOne()
    {
        var a = new float[] { 1, 2, 3 };
        var b = new float[] { 2, 4, 6 }; // same direction, 2x scale

        var result = PlagiarismDetector.CosineSimilarity(a, b);

        Assert.Equal(1f, result, precision: 4);
    }

    // --- DetectAsync integration tests (require PostgreSQL + pgvector) ---
    // These tests use FromSqlRaw which requires a real database provider.
    // Run with: dotnet test --filter "Category=Integration"

    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestQuestionId = Guid.NewGuid();

    private static CodeEmbeddingService CreateEmbeddingService(float[] embedding)
    {
        var handler = new MockHttpMessageHandler(embedding);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ollama:BaseUrl"] = "http://localhost:11434",
            ["Ollama:EmbedModel"] = "nomic-embed-text"
        }).Build();
        return new CodeEmbeddingService(http, config, NullLogger<CodeEmbeddingService>.Instance);
    }

    private static async Task<ApplicationDbContext> CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options);
        await db.SaveChangesAsync();
        return db;
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task DetectAsync_NoPriorSubmissions_ReturnsZeroScoreWithEmbedding()
    {
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var embeddingService = CreateEmbeddingService(embedding);
        await using var db = await CreateInMemoryDb();
        var detector = new PlagiarismDetector(embeddingService, db, NullLogger<PlagiarismDetector>.Instance);

        var result = await detector.DetectAsync("print(10)", "python", "user-1", TestQuestionId);

        Assert.Equal(0f, result.Score);
        Assert.NotNull(result.Embedding);
        Assert.Equal(embedding, result.Embedding);
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task DetectAsync_EmbeddingServiceReturnsNull_ReturnsZeroScoreWithNullEmbedding()
    {
        var handler = new MockHttpMessageHandler(null);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ollama:BaseUrl"] = "http://localhost:11434",
            ["Ollama:EmbedModel"] = "nomic-embed-text"
        }).Build();
        var embeddingService = new CodeEmbeddingService(http, config, NullLogger<CodeEmbeddingService>.Instance);
        await using var db = await CreateInMemoryDb();
        var detector = new PlagiarismDetector(embeddingService, db, NullLogger<PlagiarismDetector>.Instance);

        var result = await detector.DetectAsync("print(10)", "python", "user-1", TestQuestionId);

        Assert.Equal(0f, result.Score);
        Assert.Null(result.Embedding);
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task DetectAsync_PriorSubmissionWithSimilarEmbedding_ReturnsHighScore()
    {
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var embeddingService = CreateEmbeddingService(embedding);
        await using var db = await CreateInMemoryDb();

        // Seed a prior submission with a very similar embedding (from a different user)
        db.CodeSubmissions.Add(new Domain.Entities.CodeSubmission
        {
            Id = Guid.NewGuid(),
            UserId = "other-user",
            QuestionId = TestQuestionId,
            Code = "print(10)",
            Language = "python",
            IsCorrect = true,
            CodeEmbedding = System.Text.Json.JsonSerializer.Serialize(new float[] { 0.11f, 0.21f, 0.29f }),
            Status = Domain.Enums.SubmissionStatus.Accepted,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var detector = new PlagiarismDetector(embeddingService, db, NullLogger<PlagiarismDetector>.Instance);

        var result = await detector.DetectAsync("print(10)", "python", "user-1", TestQuestionId);

        Assert.True(result.Score > 0.95f, $"Expected > 0.95 but got {result.Score}");
        Assert.NotNull(result.Embedding);
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task DetectAsync_SkipsSameUserSubmissions()
    {
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var embeddingService = CreateEmbeddingService(embedding);
        await using var db = await CreateInMemoryDb();

        // Seed a prior submission from the SAME user — should be excluded
        db.CodeSubmissions.Add(new Domain.Entities.CodeSubmission
        {
            Id = Guid.NewGuid(),
            UserId = "user-1", // same user
            QuestionId = TestQuestionId,
            Code = "print(10)",
            Language = "python",
            IsCorrect = true,
            CodeEmbedding = System.Text.Json.JsonSerializer.Serialize(new float[] { 0.1f, 0.2f, 0.3f }),
            Status = Domain.Enums.SubmissionStatus.Accepted,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var detector = new PlagiarismDetector(embeddingService, db, NullLogger<PlagiarismDetector>.Instance);

        var result = await detector.DetectAsync("print(10)", "python", "user-1", TestQuestionId);

        Assert.Equal(0f, result.Score);
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task DetectAsync_MalformedJsonInCandidate_SkipsCandidateGracefully()
    {
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var embeddingService = CreateEmbeddingService(embedding);
        await using var db = await CreateInMemoryDb();

        // Seed a candidate with corrupted embedding JSON
        db.CodeSubmissions.Add(new Domain.Entities.CodeSubmission
        {
            Id = Guid.NewGuid(),
            UserId = "other-user",
            QuestionId = TestQuestionId,
            Code = "print(10)",
            Language = "python",
            IsCorrect = true,
            CodeEmbedding = "not-valid-json",
            Status = Domain.Enums.SubmissionStatus.Accepted,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var detector = new PlagiarismDetector(embeddingService, db, NullLogger<PlagiarismDetector>.Instance);

        var result = await detector.DetectAsync("print(10)", "python", "user-1", TestQuestionId);

        Assert.Equal(0f, result.Score);
        Assert.NotNull(result.Embedding);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly float[]? _embedding;

        public MockHttpMessageHandler(float[]? embedding) => _embedding = embedding;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_embedding is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var response = new
            {
                embeddings = new[] { _embedding }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response)
            });
        }
    }
}
