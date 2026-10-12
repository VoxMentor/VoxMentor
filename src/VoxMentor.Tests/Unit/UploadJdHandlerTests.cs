using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Jd.UploadJd;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="UploadJdHandler"/> covering the Ollama path,
/// heuristic fallback when Ollama fails, weight normalization on persist,
/// and the HR-skill IsTechnical flag (#117).
/// </summary>
public class UploadJdHandlerTests
{
    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; set; } = "user-1";
    }

    private sealed class FakeJdParseService : IJdParseService
    {
        public JdParseResult? Result { get; set; }

        public Task<JdParseResult?> ParseAsync(string rawText, CancellationToken cancellationToken = default)
            => Task.FromResult(Result);
    }

    private static Infrastructure.Persistence.ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Infrastructure.Persistence.ApplicationDbContext(options, new FakeCurrentUserService());
    }

    private static async Task SeedConceptsAsync(
        Infrastructure.Persistence.ApplicationDbContext db, params string[] names)
    {
        foreach (var name in names)
        {
            db.Concepts.Add(new Concept
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = $"{name} concept",
                DifficultyLevel = 2,
                Category = "Data Structures"
            });
        }
        await db.SaveChangesAsync();
    }

    private const string ValidJd =
        "Amazon is hiring an SDE-1. Strong fundamentals in DSA, proficiency in " +
        "Java/C++/Python, experience with Arrays and Strings, agile ownership.";

    private static UploadJdHandler CreateHandler(
        Infrastructure.Persistence.ApplicationDbContext db,
        FakeJdParseService? parser = null,
        FakeCurrentUserService? user = null)
    {
        return new UploadJdHandler(
            db, parser ?? new FakeJdParseService(), user ?? new FakeCurrentUserService());
    }

    [Fact]
    public async Task Handle_UnauthenticatedUser_Throws()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db, user: new FakeCurrentUserService { UserId = null });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new UploadJdCommand(ValidJd), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_OllamaSucceeds_PersistsNormalizedWeightsAndFlags()
    {
        await using var db = CreateDb();
        await SeedConceptsAsync(db, "Arrays", "Strings");
        var parser = new FakeJdParseService
        {
            Result = new JdParseResult
            {
                CompanyName = "Amazon",
                Role = "SDE-1",
                // LLM weights do NOT sum to 1 and use wrong casing + aliases
                TechnicalSkills = new Dictionary<string, float>
                {
                    ["arrays"] = 0.6f,
                    ["Strings"] = 0.4f,
                    ["Zero"] = 0f,
                    ["Negative"] = -3f
                },
                HrSkills = new Dictionary<string, float>
                {
                    ["Leadership"] = 2f,
                    ["Ownership"] = 1f
                },
                Difficulty = "Medium-Hard",
                EstimatedWeeks = 8
            }
        };
        var handler = CreateHandler(db, parser);

        var response = await handler.Handle(new UploadJdCommand(ValidJd), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(1f, response.Data.TechnicalSkills.Values.Sum(), 5); // renormalized
        Assert.Equal(1f, response.Data.HrSkills.Values.Sum(), 5);
        Assert.Equal("Arrays", response.Data.TechnicalSkills.Keys.Single(k => k == "Arrays")); // canonical casing
        Assert.DoesNotContain("Zero", response.Data.TechnicalSkills);
        Assert.DoesNotContain("Negative", response.Data.TechnicalSkills);

        var jd = await db.JobDescriptions.SingleAsync(j => j.Id == response.Data.JdId);
        Assert.Equal("user-1", jd.UserId);
        Assert.Equal("Amazon", jd.CompanyName);

        var weights = await db.JdSkillWeights
            .Where(w => w.JobDescriptionId == jd.Id)
            .ToListAsync();
        Assert.Equal(2, weights.Count(w => w.IsTechnical));
        Assert.Equal(2, weights.Count(w => !w.IsTechnical)); // HR rows flagged
        Assert.Equal(1f, weights.Where(w => w.IsTechnical).Sum(w => w.Weight), 5);
        Assert.Equal(1f, weights.Where(w => !w.IsTechnical).Sum(w => w.Weight), 5);
    }

    [Fact]
    public async Task Handle_OllamaFails_FallsBackToHeuristics()
    {
        await using var db = CreateDb();
        await SeedConceptsAsync(db, "Arrays", "Strings", "Binary Trees");
        var handler = CreateHandler(db, parser: new FakeJdParseService { Result = null });

        var response = await handler.Handle(new UploadJdCommand(ValidJd), CancellationToken.None);

        Assert.True(response.Success); // degrade, never 503
        Assert.Equal("Amazon", response.Data.CompanyName);
        Assert.NotEmpty(response.Data.TechnicalSkills);
        Assert.Equal(1f, response.Data.TechnicalSkills.Values.Sum(), 5);
    }

    [Fact]
    public async Task Handle_DuplicateCanonicalTargets_MergedNotThrowing()
    {
        await using var db = CreateDb();
        await SeedConceptsAsync(db, "Knapsack Problem");
        var parser = new FakeJdParseService
        {
            Result = new JdParseResult
            {
                TechnicalSkills = new Dictionary<string, float>
                {
                    // Both alias to "Knapsack Problem" — must merge, not throw
                    ["DP"] = 0.6f,
                    ["Dynamic Programming"] = 0.4f
                }
            }
        };
        var handler = CreateHandler(db, parser);

        var response = await handler.Handle(new UploadJdCommand(ValidJd), CancellationToken.None);

        var skills = response.Data.TechnicalSkills;
        Assert.Single(skills);
        Assert.Equal(1f, skills["Knapsack Problem"], 5);

        var jdWeights = await db.JdSkillWeights
            .Where(w => w.JobDescriptionId == response.Data.JdId)
            .ToListAsync();
        Assert.Single(jdWeights); // unique index (JobDescriptionId, SkillName) safe
    }

    [Fact]
    public async Task Handle_SkillInBothMaps_OnlyTechnicalRowPersisted()
    {
        // The unique index (JobDescriptionId, SkillName) ignores IsTechnical —
        // an overlapping key from both maps would 500 the save. Technical wins;
        // HR renormalizes over its survivors (#117 review).
        await using var db = CreateDb();
        var parser = new FakeJdParseService
        {
            Result = new JdParseResult
            {
                TechnicalSkills = new Dictionary<string, float> { ["Leadership"] = 1f },
                HrSkills = new Dictionary<string, float>
                {
                    ["Leadership"] = 0.9f,
                    ["Ownership"] = 0.1f
                }
            }
        };
        var handler = CreateHandler(db, parser);

        var response = await handler.Handle(new UploadJdCommand(ValidJd), CancellationToken.None);

        var rows = await db.JdSkillWeights
            .Where(w => w.JobDescriptionId == response.Data.JdId)
            .ToListAsync();
        Assert.Equal(2, rows.Count); // Leadership(tech) + Ownership(HR) — no dup pair
        Assert.Single(rows, w => w.SkillName == "Leadership" && w.IsTechnical);
        Assert.Single(rows, w => w.SkillName == "Ownership" && !w.IsTechnical);
        Assert.Equal(1f, response.Data.HrSkills.Values.Sum(), 5); // HR still sums to 1.0
        Assert.DoesNotContain("Leadership", response.Data.HrSkills);
    }

    [Fact]
    public async Task Handle_OversizedLlmStrings_ClampedToColumnLimits()
    {
        // LLM strings are untrusted against varchar(200/200/50/100) — an
        // oversized value must truncate, not explode into DbUpdateException 500
        // (#117 review). InMemory provider can't enforce lengths, so assert the
        // values handed to it.
        await using var db = CreateDb();
        var parser = new FakeJdParseService
        {
            Result = new JdParseResult
            {
                CompanyName = new string('C', 300),
                Role = new string('R', 300),
                Difficulty = new string('D', 80),
                TechnicalSkills = new Dictionary<string, float>
                {
                    [new string('T', 150)] = 1f
                },
                HrSkills = new Dictionary<string, float>
                {
                    [new string('H', 150)] = 1f
                }
            }
        };
        var handler = CreateHandler(db, parser);

        var response = await handler.Handle(new UploadJdCommand(ValidJd), CancellationToken.None);

        Assert.Equal(200, response.Data.CompanyName.Length);
        Assert.Equal(200, response.Data.Role.Length);
        Assert.Equal(50, response.Data.Difficulty.Length);
        Assert.All(response.Data.TechnicalSkills.Keys, k => Assert.True(k.Length <= 100));
        Assert.All(response.Data.HrSkills.Keys, k => Assert.True(k.Length <= 100));

        var jd = await db.JobDescriptions.SingleAsync(j => j.Id == response.Data.JdId);
        Assert.Equal(200, jd.CompanyName.Length);
        var rows = await db.JdSkillWeights
            .Where(w => w.JobDescriptionId == jd.Id)
            .ToListAsync();
        Assert.All(rows, w => Assert.True(w.SkillName.Length <= 100));
    }

    [Fact]
    public async Task Handle_ZeroSkills_ReturnsEmptyMapsAndStillPersistsJd()
    {
        await using var db = CreateDb();
        // no concepts seeded → heuristic finds nothing
        var handler = CreateHandler(db);

        var response = await handler.Handle(new UploadJdCommand(ValidJd), CancellationToken.None);

        Assert.True(response.Success); // Q5: empty maps are valid, not an error
        Assert.Empty(response.Data.TechnicalSkills);
        Assert.Empty(response.Data.HrSkills);
        Assert.True(await db.JobDescriptions.AnyAsync(j => j.Id == response.Data.JdId));
        Assert.Empty(await db.JdSkillWeights
            .Where(w => w.JobDescriptionId == response.Data.JdId).ToListAsync());
    }
}
