using VoxMentor.Application.Features.Jd.GetJd;
using VoxMentor.Application.Features.Practice.GetReadiness;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="RoadmapBuilder"/> covering week bucketing, the
/// estimatedWeeks boundaries, and first-wins concept resolution.
/// Pure in-memory data — no EF.
/// </summary>
public class RoadmapBuilderTests
{
    private static Concept MakeConcept(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = $"{name} concept",
        DifficultyLevel = 2,
        Category = "Data Structures"
    };

    private static SkillGapDto MakeGap(string topic, float severity) => new()
    {
        Topic = topic,
        Severity = severity,
        Recommendation = $"Practice {topic}."
    };

    /// <summary>Gaps in severity-descending order, as ReadinessCalculator emits them.</summary>
    private static List<SkillGapDto> MakeGaps(params string[] topics) =>
        topics.Select(t => MakeGap(t, 0.5f)).ToList();

    [Fact]
    public void Build_EmptyGaps_ReturnsEmpty()
    {
        var concepts = new[] { MakeConcept("DP") };

        var roadmap = RoadmapBuilder.Build(new List<SkillGapDto>(), concepts, 6);

        Assert.Empty(roadmap);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_EstimatedWeeksBelowOne_ReturnsEmptyWithoutDivideByZero(int estimatedWeeks)
    {
        var gaps = MakeGaps("DP", "Graphs", "Trees");

        var roadmap = RoadmapBuilder.Build(gaps, Array.Empty<Concept>(), estimatedWeeks);

        Assert.Empty(roadmap); // guard precedes both divisions; no DivideByZeroException
    }

    [Fact]
    public void Build_SingleGap_SingleWeek()
    {
        var dp = MakeConcept("DP");

        var roadmap = RoadmapBuilder.Build(MakeGaps("DP"), new[] { dp }, 6);

        var week = Assert.Single(roadmap);
        Assert.Equal(1, week.Week);
        Assert.Equal("DP", week.Focus);
        Assert.Equal(dp.Id, Assert.Single(week.ConceptIds));
    }

    [Fact]
    public void Build_FewerTopicsThanWeeks_OmitsTrailingEmptyWeeks()
    {
        var concepts = new[] { MakeConcept("DP"), MakeConcept("Graphs") };

        // 2 gaps over 6 weeks -> one topic per week, but only 2 non-empty weeks.
        var roadmap = RoadmapBuilder.Build(MakeGaps("DP", "Graphs"), concepts, 6);

        Assert.Equal(2, roadmap.Count);
        Assert.Equal(new[] { 1, 2 }, roadmap.Select(w => w.Week));
        Assert.Equal("DP", roadmap[0].Focus);
        Assert.Equal("Graphs", roadmap[1].Focus);
    }

    [Fact]
    public void Build_MoreTopicsThanWeeks_ChunksWithoutDropping()
    {
        string[] topics = { "DP", "Graphs", "Trees", "Arrays", "Strings", "Heaps" };
        var concepts = topics.Select(MakeConcept).ToArray();

        var roadmap = RoadmapBuilder.Build(MakeGaps(topics), concepts, 2);

        Assert.Equal(2, roadmap.Count); // ceil(6 / 2) -> 3 topics per week
        Assert.All(roadmap, w => Assert.Equal(3, w.ConceptIds.Count));

        var focused = roadmap.SelectMany(w => w.Focus.Split(", ")).ToList();
        Assert.Equal(topics.OrderBy(t => t), focused.OrderBy(t => t)); // none dropped
    }

    [Fact]
    public void Build_UnevenChunks_LastWeekHoldsTheRemainder()
    {
        string[] topics = { "DP", "Graphs", "Trees", "Arrays", "Strings", "Heaps", "Greedy" };
        var concepts = topics.Select(MakeConcept).ToArray();

        var roadmap = RoadmapBuilder.Build(MakeGaps(topics), concepts, 3);

        Assert.Equal(3, roadmap.Count); // ceil(7 / 3) -> 3 per week
        Assert.Equal(new[] { 3, 3, 1 }, roadmap.Select(w => w.ConceptIds.Count)); // 7 covered
    }

    [Fact]
    public void Build_DuplicateConceptNames_ResolvesFirstWins()
    {
        var first = MakeConcept("DP");
        var duplicate = MakeConcept("DP"); // Concept.Name has no unique index
        var gaps = MakeGaps("DP");

        var roadmap = RoadmapBuilder.Build(gaps, new[] { first, duplicate }, 4);

        // One id for the topic, and it is the FIRST row — matching
        // ReadinessCalculator's GroupBy(Name).First(), so the roadmap never
        // points at a concept whose mastery the score ignored.
        var week = Assert.Single(roadmap);
        Assert.Equal(first.Id, Assert.Single(week.ConceptIds));
    }

    [Fact]
    public void Build_TopicWithoutMatchingConcept_KeepsFocusWithEmptyConceptIds()
    {
        var gaps = MakeGaps("Negotiation"); // HR skill with no concept vocabulary

        var roadmap = RoadmapBuilder.Build(gaps, Array.Empty<Concept>(), 4);

        var week = Assert.Single(roadmap);
        Assert.Equal("Negotiation", week.Focus);
        Assert.Empty(week.ConceptIds);
    }

    [Fact]
    public void Build_ConceptIdsScopedToThatWeeksFocusOnly()
    {
        var dp = MakeConcept("DP");
        var graphs = MakeConcept("Graphs");

        var roadmap = RoadmapBuilder.Build(MakeGaps("DP", "Graphs"), new[] { dp, graphs }, 2);

        Assert.Equal(dp.Id, Assert.Single(roadmap[0].ConceptIds)); // week 1: DP only
        Assert.Equal(graphs.Id, Assert.Single(roadmap[1].ConceptIds)); // week 2: Graphs only
    }
}
