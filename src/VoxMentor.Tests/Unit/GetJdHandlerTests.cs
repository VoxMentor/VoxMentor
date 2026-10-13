using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Jd.GetJd;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="GetJdHandler"/> covering ownership 404s,
/// readiness/gap pass-through, and the #118 week-by-week roadmap.
/// </summary>
public class GetJdHandlerTests
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
        return new Infrastructure.Persistence.ApplicationDbContext(options, new FakeCurrentUserService());
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
            EstimatedWeeks = estimatedWeeks,
            CreatedAt = DateTime.UtcNow
        };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();
        return jd;
    }

    private static async Task SeedSkillWeightAsync(
        Infrastructure.Persistence.ApplicationDbContext db,
        Guid jdId,
        string skillName,
        float weight,
        bool isTechnical = true)
    {
        db.JdSkillWeights.Add(new JdSkillWeight
        {
            Id = Guid.NewGuid(),
            JobDescriptionId = jdId,
            SkillName = skillName,
            Weight = weight,
            IsTechnical = isTechnical
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

    private static GetJdHandler CreateHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeCurrentUserService? user = null)
    {
        return new GetJdHandler(db, user ?? new FakeCurrentUserService());
    }

    [Fact]
    public async Task Handle_UnauthenticatedUser_Throws()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        var handler = CreateHandler(db, new FakeCurrentUserService { UserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new GetJdQuery(jd.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_JdNotFound_ThrowsNotFound()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetJdQuery(Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("job description", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_JdNotOwnedByUser_ThrowsNotFound()
    {
        await using var db = CreateDb();
        var foreignJd = await SeedJdAsync(db, userId: "other-user");
        var handler = CreateHandler(db); // current user = "user-1"

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetJdQuery(foreignJd.Id), CancellationToken.None));
        Assert.Contains("job description", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_ZeroMastery_HighestSeverityTopicIsWeek1()
    {
        await using var db = CreateDb();
        var dp = await SeedConceptAsync(db, "DP");
        var graphs = await SeedConceptAsync(db, "Graphs");
        var jd = await SeedJdAsync(db, "user-1", estimatedWeeks: 4);
        await SeedSkillWeightAsync(db, jd.Id, "DP", 0.7f);
        await SeedSkillWeightAsync(db, jd.Id, "Graphs", 0.3f);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetJdQuery(jd.Id), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(0f, response.Data!.ReadinessScore); // no mastery rows at all

        // Highest severity (DP, weight 0.7) must be week 1's focus.
        Assert.Equal("DP", response.Data.Roadmap[0].Focus);
        Assert.Contains(dp.Id, response.Data.Roadmap[0].ConceptIds);
        Assert.DoesNotContain(graphs.Id, response.Data.Roadmap[0].ConceptIds);

        // 2 topics over 4 weeks -> only 2 non-empty weeks emitted.
        Assert.Equal(2, response.Data.Roadmap.Count);
        Assert.Equal("Graphs", response.Data.Roadmap[1].Focus);
        Assert.Equal(new[] { 1, 2 }, response.Data.Roadmap.Select(w => w.Week));
    }

    [Fact]
    public async Task Handle_MoreTopicsThanWeeks_ChunksWithoutDropping()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1", estimatedWeeks: 2);
        string[] topics = { "DP", "Graphs", "Trees", "Arrays", "Strings", "Heaps" };
        foreach (var topic in topics)
        {
            await SeedConceptAsync(db, topic);
            await SeedSkillWeightAsync(db, jd.Id, topic, 1f / topics.Length);
        }
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetJdQuery(jd.Id), CancellationToken.None);

        var roadmap = response.Data!.Roadmap;
        Assert.Equal(2, roadmap.Count);                 // ceil(6 / 2) -> 3 per week
        Assert.Equal(3, roadmap[0].ConceptIds.Count);
        Assert.Equal(3, roadmap[1].ConceptIds.Count);

        // Every topic appears exactly once across the roadmap — none dropped.
        var focused = roadmap.SelectMany(w => w.Focus.Split(", ")).ToList();
        Assert.Equal(topics.OrderBy(t => t), focused.OrderBy(t => t));
    }

    [Fact]
    public async Task Handle_FullMastery_EmptyRoadmapAndGaps()
    {
        await using var db = CreateDb();
        var concept = await SeedConceptAsync(db, "DP");
        var jd = await SeedJdAsync(db, "user-1");
        await SeedSkillWeightAsync(db, jd.Id, "DP", 1f);
        await SeedMasteryAsync(db, "user-1", concept.Id, 1f);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetJdQuery(jd.Id), CancellationToken.None);

        Assert.Equal(100f, response.Data!.ReadinessScore);
        Assert.Empty(response.Data.Gaps);
        Assert.Empty(response.Data.Roadmap);
    }

    [Fact]
    public async Task Handle_JdWithNoSkills_ReturnsZeroScoreAndEmptyCollections()
    {
        // Empty skill maps are valid on upload (JdController), so a GET on such
        // a JD is a documented 200 — never an error.
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetJdQuery(jd.Id), CancellationToken.None);
        var dto = response.Data!;

        Assert.True(response.Success);
        Assert.Equal(0f, dto.ReadinessScore);
        Assert.Empty(dto.TechnicalSkills);
        Assert.Empty(dto.HrSkills);
        Assert.Empty(dto.Gaps);
        Assert.Empty(dto.Roadmap);
    }

    [Fact]
    public async Task Handle_SkillWithoutMatchingConcept_AppearsInMapsButNotInScoreOrRoadmap()
    {
        // ReadinessCalculator skips skills with no matching Concept row, but the
        // handler builds the skill maps from the raw weights — so an HR-style
        // skill outside the concept vocabulary shows in technicalSkills/hrSkills
        // while contributing nothing to readiness, gaps, or the roadmap.
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        await SeedSkillWeightAsync(db, jd.Id, "Negotiation", 0.5f, isTechnical: false);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetJdQuery(jd.Id), CancellationToken.None);
        var dto = response.Data!;

        Assert.Equal(0.5f, dto.HrSkills["Negotiation"], 3); // present in the map
        Assert.Equal(0f, dto.ReadinessScore);               // ...but never scored
        Assert.Empty(dto.Gaps);
        Assert.Empty(dto.Roadmap);
    }

    [Fact]
    public async Task Handle_HighestSeverityHrGap_WinsWeek1()
    {
        // Gaps are built from ALL weights (no IsTechnical filter), so an HR topic
        // outranking every technical topic legitimately opens the roadmap. Pins
        // that behavior against a future "technical-only roadmap" regression.
        await using var db = CreateDb();
        var leadership = await SeedConceptAsync(db, "Leadership");
        var dp = await SeedConceptAsync(db, "DP");
        var jd = await SeedJdAsync(db, "user-1", estimatedWeeks: 4);
        await SeedSkillWeightAsync(db, jd.Id, "Leadership", 0.9f, isTechnical: false);
        await SeedSkillWeightAsync(db, jd.Id, "DP", 0.1f, isTechnical: true);
        await SeedMasteryAsync(db, "user-1", dp.Id, 0.5f); // Leadership: no mastery row
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetJdQuery(jd.Id), CancellationToken.None);
        var roadmap = response.Data!.Roadmap;

        // severity: Leadership 0.9*(1-0) = 0.9 > DP 0.1*(1-0.5) = 0.05
        Assert.Equal("Leadership", roadmap[0].Focus);
        Assert.Equal(leadership.Id, Assert.Single(roadmap[0].ConceptIds));
        Assert.Equal("DP", roadmap[1].Focus);
    }

    [Fact]
    public async Task Handle_MapsCreatedAtFromJd()
    {
        await using var db = CreateDb();
        var jd = await SeedJdAsync(db, "user-1");
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetJdQuery(jd.Id), CancellationToken.None);

        Assert.Equal(jd.CreatedAt, response.Data!.CreatedAt);
    }

    [Fact]
    public async Task Handle_SplitsTechnicalAndHrSkills_AndMatchesApiShape()
    {
        await using var db = CreateDb();
        var dp = await SeedConceptAsync(db, "DP");
        var leadership = await SeedConceptAsync(db, "Leadership");
        var jd = await SeedJdAsync(db, "user-1");
        await SeedSkillWeightAsync(db, jd.Id, "DP", 0.6f, isTechnical: true);
        await SeedSkillWeightAsync(db, jd.Id, "Leadership", 0.4f, isTechnical: false);
        await SeedMasteryAsync(db, "user-1", dp.Id, 0.5f);
        await SeedMasteryAsync(db, "user-1", leadership.Id, 1f);
        var handler = CreateHandler(db);

        var response = await handler.Handle(new GetJdQuery(jd.Id), CancellationToken.None);
        var dto = response.Data!;

        // score = 100 * (0.5*0.6 + 1*0.4) = 70
        Assert.Equal(70f, dto.ReadinessScore, 1);
        Assert.Equal(jd.Id, dto.JdId);
        Assert.Equal("Amazon", dto.CompanyName);
        Assert.Equal("SDE-1", dto.Role);
        Assert.Equal("Medium-Hard", dto.Difficulty);
        Assert.Equal(6, dto.EstimatedWeeks);

        // Skill maps split by IsTechnical.
        Assert.Single(dto.TechnicalSkills);
        Assert.Equal(0.6f, dto.TechnicalSkills["DP"], 3);
        Assert.Single(dto.HrSkills);
        Assert.Equal(0.4f, dto.HrSkills["Leadership"], 3);

        // Only DP (mastery 0.5 < 1) is a gap; full-mastery HR skill is not.
        var gap = Assert.Single(dto.Gaps);
        Assert.Equal("DP", gap.Topic);
        Assert.Equal(0.3f, gap.Severity, 3); // 0.6 * (1 - 0.5)
        Assert.False(string.IsNullOrWhiteSpace(gap.Recommendation));
    }
}
