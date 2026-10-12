using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ReadinessCalculator"/> covering weighted score,
/// gap severity ordering, unmatched skills, and the empty-weights path.
/// Pure in-memory data — no EF.
/// </summary>
public class ReadinessCalculatorTests
{
    private static Concept MakeConcept(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = $"{name} concept",
        DifficultyLevel = 2,
        Category = "Data Structures"
    };

    private static JdSkillWeight MakeWeight(string skillName, float weight) => new()
    {
        Id = Guid.NewGuid(),
        JobDescriptionId = Guid.NewGuid(),
        SkillName = skillName,
        Weight = weight,
        IsTechnical = true
    };

    private static StudentMastery MakeMastery(Guid conceptId, float probability) => new()
    {
        Id = Guid.NewGuid(),
        UserId = "user-1",
        ConceptId = conceptId,
        MasteryProbability = probability,
        CorrectAttempts = 3,
        IncorrectAttempts = 1,
        LastPracticedAt = DateTime.UtcNow
    };

    [Fact]
    public void Compute_NoSkillWeights_ReturnsZeroScoreAndEmptyLists()
    {
        var result = ReadinessCalculator.Compute(
            Array.Empty<JdSkillWeight>(),
            new[] { MakeConcept("DP") },
            new Dictionary<Guid, StudentMastery>(),
            estimatedWeeks: 4);

        Assert.Equal(0f, result.Score);
        Assert.Empty(result.Breakdown);
        Assert.Empty(result.Gaps);
        Assert.Equal(4, result.EstimatedWeeksToReady);
    }

    [Fact]
    public void Compute_PartialMastery_ReturnsCorrectWeightedScore()
    {
        var dp = MakeConcept("DP");
        var graphs = MakeConcept("Graphs");
        var weights = new[] { MakeWeight("DP", 0.6f), MakeWeight("Graphs", 0.4f) };
        var masteries = new Dictionary<Guid, StudentMastery>
        {
            [dp.Id] = MakeMastery(dp.Id, 0.5f),
            [graphs.Id] = MakeMastery(graphs.Id, 0.8f)
        };

        var result = ReadinessCalculator.Compute(
            weights, new[] { dp, graphs }, masteries, estimatedWeeks: 6);

        // score = 100 * (0.5*0.6 + 0.8*0.4) = 62
        Assert.Equal(62f, result.Score, 1);
        Assert.Equal(2, result.Breakdown.Count);
        Assert.Equal(0.3f, result.Breakdown[0].Contribution, 1); // 0.5 * 0.6
        // :P0 is culture-sensitive ("30 %" under InvariantCulture/fr-FR) — build the
        // expected through the same ambient path so only the template is pinned;
        // the severity value is asserted above.
        Assert.Equal(
            $"Practice DP — {result.Gaps[0].Severity:P0} gap remaining.",
            result.Gaps[0].Recommendation);
        Assert.Equal(6, result.EstimatedWeeksToReady);
    }

    [Fact]
    public void Compute_WeightsNotSummingToOne_DoesNotNormalize()
    {
        var dp = MakeConcept("DP");
        var graphs = MakeConcept("Graphs");
        var weights = new[] { MakeWeight("DP", 0.6f), MakeWeight("Graphs", 0.3f) };
        var masteries = new Dictionary<Guid, StudentMastery>
        {
            [dp.Id] = MakeMastery(dp.Id, 1f),
            [graphs.Id] = MakeMastery(graphs.Id, 1f)
        };

        var result = ReadinessCalculator.Compute(
            weights, new[] { dp, graphs }, masteries, estimatedWeeks: 5);

        // weights sum to 0.9: raw weighted sum, not renormalized to 100
        Assert.Equal(90f, result.Score, 1);
        Assert.Empty(result.Gaps); // full mastery on both skills
    }

    [Fact]
    public void Compute_GapsOrderedBySeverityDescending()
    {
        var dp = MakeConcept("DP");
        var graphs = MakeConcept("Graphs");
        var weights = new[] { MakeWeight("DP", 0.6f), MakeWeight("Graphs", 0.4f) };
        var masteries = new Dictionary<Guid, StudentMastery>
        {
            [dp.Id] = MakeMastery(dp.Id, 0.5f),      // severity = 0.6 * 0.5 = 0.30
            [graphs.Id] = MakeMastery(graphs.Id, 0.8f) // severity = 0.4 * 0.2 = 0.08
        };

        var result = ReadinessCalculator.Compute(
            weights, new[] { dp, graphs }, masteries, estimatedWeeks: 6);

        Assert.Equal(2, result.Gaps.Count);
        Assert.Equal("DP", result.Gaps[0].Topic);
        Assert.Equal(0.3f, result.Gaps[0].Severity, 1);
        Assert.Equal("Graphs", result.Gaps[1].Topic);
    }

    [Fact]
    public void Compute_FullMastery_Returns100AndNoGaps()
    {
        var dp = MakeConcept("DP");
        var weights = new[] { MakeWeight("DP", 1f) };
        var masteries = new Dictionary<Guid, StudentMastery>
        {
            [dp.Id] = MakeMastery(dp.Id, 1f)
        };

        var result = ReadinessCalculator.Compute(
            weights, new[] { dp }, masteries, estimatedWeeks: 2);

        Assert.Equal(100f, result.Score);
        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void Compute_SkillWithoutMatchingConcept_IsSkipped()
    {
        var dp = MakeConcept("DP");
        var weights = new[] { MakeWeight("DP", 1f), MakeWeight("UnknownSkill", 1f) };
        var masteries = new Dictionary<Guid, StudentMastery>
        {
            [dp.Id] = MakeMastery(dp.Id, 0.7f)
        };

        var result = ReadinessCalculator.Compute(
            weights, new[] { dp }, masteries, estimatedWeeks: 3);

        Assert.Single(result.Breakdown);
        Assert.Equal("DP", result.Breakdown[0].Topic);
        Assert.Equal(70f, result.Score, 1);
    }

    [Fact]
    public void Compute_SkillWithoutMasteryRow_CountsAsZeroMastery()
    {
        var dp = MakeConcept("DP");
        var weights = new[] { MakeWeight("DP", 0.5f) };

        var result = ReadinessCalculator.Compute(
            weights, new[] { dp }, new Dictionary<Guid, StudentMastery>(), estimatedWeeks: 1);

        Assert.Equal(0f, result.Score);
        Assert.Single(result.Gaps);
        Assert.Equal(0.5f, result.Gaps[0].Severity, 1);
    }

    [Fact]
    public void Compute_DuplicateConceptNames_FirstWins()
    {
        var first = MakeConcept("DP");
        var duplicate = MakeConcept("DP");
        var weights = new[] { MakeWeight("DP", 1f) };
        var masteries = new Dictionary<Guid, StudentMastery>
        {
            [first.Id] = MakeMastery(first.Id, 0.4f)
        };

        var result = ReadinessCalculator.Compute(
            weights, new[] { first, duplicate }, masteries, estimatedWeeks: 1);

        Assert.Equal(40f, result.Score, 1);
    }
}
