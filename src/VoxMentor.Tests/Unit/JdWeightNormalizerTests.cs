using VoxMentor.Application.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="JdWeightNormalizer"/>: junk-weight hygiene and
/// the sum-to-1.0 guarantee (#117 — never trust the LLM's arithmetic).
/// </summary>
public class JdWeightNormalizerTests
{
    [Fact]
    public void Normalize_UnnormalizedWeights_SumToOne()
    {
        var result = JdWeightNormalizer.Normalize(new Dictionary<string, float>
        {
            ["DP"] = 5f,
            ["Graphs"] = 3f,
            ["Arrays"] = 2f
        });

        Assert.Equal(3, result.Count);
        Assert.Equal(1f, result.Values.Sum(), 5);
        Assert.Equal(0.5f, result["DP"], 5);
        Assert.Equal(0.3f, result["Graphs"], 5);
        Assert.Equal(0.2f, result["Arrays"], 5);
    }

    [Fact]
    public void Normalize_NegativeAndZeroWeights_Dropped()
    {
        var result = JdWeightNormalizer.Normalize(new Dictionary<string, float>
        {
            ["DP"] = 0.6f,
            ["Graphs"] = 0f,
            ["Arrays"] = -2f
        });

        Assert.Single(result);
        Assert.Equal(1f, result["DP"]);
    }

    [Fact]
    public void Normalize_NaNAndInfinity_Dropped()
    {
        var result = JdWeightNormalizer.Normalize(new Dictionary<string, float>
        {
            ["DP"] = 1f,
            ["NaN"] = float.NaN,
            ["Inf"] = float.PositiveInfinity
        });

        Assert.Single(result);
        Assert.Equal(1f, result["DP"]);
    }

    [Fact]
    public void Normalize_EmptyMap_ReturnsEmpty()
    {
        var result = JdWeightNormalizer.Normalize(new Dictionary<string, float>());

        Assert.Empty(result);
    }

    [Fact]
    public void Normalize_AllJunkWeights_ReturnsEmpty()
    {
        var result = JdWeightNormalizer.Normalize(new Dictionary<string, float>
        {
            ["DP"] = -1f,
            ["Graphs"] = 0f
        });

        Assert.Empty(result); // no divide-by-zero, no invented skills
    }

    [Fact]
    public void Normalize_SingleEntry_BecomesOne()
    {
        var result = JdWeightNormalizer.Normalize(new Dictionary<string, float> { ["DP"] = 0.37f });

        Assert.Equal(1f, result["DP"]);
    }

    [Fact]
    public void Normalize_BlankKey_Dropped()
    {
        var result = JdWeightNormalizer.Normalize(new Dictionary<string, float>
        {
            ["DP"] = 1f,
            ["   "] = 5f
        });

        Assert.Single(result);
        Assert.Equal(1f, result["DP"]);
    }

    [Fact]
    public void Normalize_FiniteWeightsThatOverflowSum_FallBackToUniform()
    {
        // Each weight passes IsFinite, but their float sum overflows to +Inf —
        // without the guard every value/Inf = 0 and the map would sum to 0.0,
        // silently breaking the sum-to-1.0 contract (#117 review).
        var result = JdWeightNormalizer.Normalize(new Dictionary<string, float>
        {
            ["A"] = 3e38f,
            ["B"] = 3e38f,
            ["C"] = 3e38f
        });

        Assert.Equal(3, result.Count);
        Assert.Equal(1f, result.Values.Sum(), 5);
        Assert.Equal(1f / 3f, result["A"], 5);
    }

    [Fact]
    public void Normalize_WhitespaceKeyVariants_Merged()
    {
        // ponytail: canonicalization can produce " DP" and "DP" — the DB
        // unique index (JobDescriptionId, SkillName) would reject the pair.
        var result = JdWeightNormalizer.Normalize(new Dictionary<string, float>
        {
            ["DP"] = 0.4f,
            [" DP "] = 0.6f
        });

        Assert.Single(result);
        Assert.Equal(1f, result["DP"]);
    }
}
