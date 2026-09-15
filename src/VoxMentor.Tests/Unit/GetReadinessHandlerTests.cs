using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Practice.GetReadiness;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="GetReadinessHandler"/> covering authentication,
/// missing JDs, weighted scoring, gap analysis, and default-JD selection.
/// </summary>
public class GetReadinessHandlerTests
{
    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
    }

    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options);
    }

    private static async Task<Concept> SeedConceptAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string name = "Arrays")
    {
        var concept = new Concept
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = $"{name} concept",
            DifficultyLevel = 2,
            Category = "Data Structures"
        };
        db.Concepts.Add(concept);
        await db.SaveChangesAsync();
        return concept;
    }

    private static async Task<JobDescription> SeedJdAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string userId,
        string company = "Amazon",
        int estimatedWeeks = 6)
    {
        var jd = new JobDescription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompanyName = company,
            Role = "SDE-1",
            RawText = $"{company} is hiring...",
            Difficulty = "Medium-Hard",
            EstimatedWeeks = estimatedWeeks
        };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();
        return jd;
    }

    private static async Task SeedSkillWeightAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        Guid jdId,
        string skillName,
        float weight)
    {
        db.JdSkillWeights.Add(new JdSkillWeight
        {
            Id = Guid.NewGuid(),
            JobDescriptionId = jdId,
            SkillName = skillName,
            Weight = weight,
            IsTechnical = true
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedMasteryAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        string userId,
        Guid conceptId,
        float masteryProbability)
    {
        db.StudentMasteries.Add(new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConceptId = conceptId,
            MasteryProbability = masteryProbability,
            CorrectAttempts = 3,
            IncorrectAttempts = 1,
            LastPracticedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static GetReadinessHandler CreateHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeCurrentUserService? user = null)
    {
        return new GetReadinessHandler(db, user ?? new FakeCurrentUserService());
    }

    [Fact]
    public async Task Handle_UnauthenticatedUser_Throws()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db, new FakeCurrentUserService { UserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new GetReadinessQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NoJd_ThrowsNotFound()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetReadinessQuery(), CancellationToken.None));
        Assert.Contains("job description", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_JdNotFound_ThrowsNotFound()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetReadinessQuery { JdId = Guid.NewGuid() }, CancellationToken.None));
        Assert.Contains("job description", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_JdNotOwnedByUser_ThrowsNotFound()
    {
        await using var db = CreateDb();
        await SeedJdAsync(db, userId: "other-user");
        var handler = CreateHandler(db);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetReadinessQuery(), CancellationToken.None));
        Assert.Contains("job description", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_JdWithNoSkills_ReturnsZeroScore()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetReadinessQuery { JdId = jd.Id }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(0f, response.Data.Score);
        Assert.Empty(response.Data.Breakdown);
        Assert.Empty(response.Data.Gaps);
    }

    [Fact]
    public async Task Handle_PartialMastery_CorrectWeightedScore()
    {
        await using var db = CreateDb();
        var dp = await SeedConceptAsync(db, "DP");
        var graphs = await SeedConceptAsync(db, "Graphs");
        var jd = await SeedJdAsync(db, "user-1");
        await SeedSkillWeightAsync(db, jd.Id, "DP", 0.6f);
        await SeedSkillWeightAsync(db, jd.Id, "Graphs", 0.4f);
        await SeedMasteryAsync(db, "user-1", dp.Id, 0.5f);
        await SeedMasteryAsync(db, "user-1", graphs.Id, 0.8f);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetReadinessQuery { JdId = jd.Id }, CancellationToken.None);

        // score = 100 * (0.5*0.6 + 0.8*0.4) = 100 * (0.3 + 0.32) = 62
        Assert.Equal(62f, response.Data.Score, 1);
        Assert.Equal(2, response.Data.Breakdown.Count);
        Assert.Equal(2, response.Data.Gaps.Count); // Both DP (0.5) and Graphs (0.8) < 1.0
        Assert.Equal("DP", response.Data.Gaps[0].Topic); // higher severity first
        Assert.Equal(0.3f, response.Data.Gaps[0].Severity, 1); // 0.6 * (1-0.5)
    }

    [Fact]
    public async Task Handle_FullMastery_Returns100()
    {
        await using var db = CreateDb();
        var concept = await SeedConceptAsync(db, "DP");
        var jd = await SeedJdAsync(db, "user-1");
        await SeedSkillWeightAsync(db, jd.Id, "DP", 1f);
        await SeedMasteryAsync(db, "user-1", concept.Id, 1f);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetReadinessQuery { JdId = jd.Id }, CancellationToken.None);

        Assert.Equal(100f, response.Data.Score);
        Assert.Empty(response.Data.Gaps);
    }

    [Fact]
    public async Task Handle_NoJdId_DefaultsToLatestJd()
    {
        await using var db = CreateDb();
        var concept = await SeedConceptAsync(db, "DP");
        var oldJd = await SeedJdAsync(db, "user-1", "OldCorp", estimatedWeeks: 2);
        var newJd = await SeedJdAsync(db, "user-1", "NewCorp", estimatedWeeks: 8);
        await SeedSkillWeightAsync(db, oldJd.Id, "DP", 0.5f);
        await SeedSkillWeightAsync(db, newJd.Id, "DP", 0.7f);
        await SeedMasteryAsync(db, "user-1", concept.Id, 0.4f);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetReadinessQuery(), CancellationToken.None);

        // Should use newJd (latest): score = 100 * 0.4 * 0.7 = 28
        Assert.Equal(28f, response.Data.Score, 1);
        Assert.Equal(8, response.Data.EstimatedWeeksToReady);
    }
}
