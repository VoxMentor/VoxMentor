using VoxMentor.Application.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="JdHeuristics"/>: concept-name matching,
/// canonicalization (exact → case-insensitive → alias), and the
/// keyword-heuristic parse used when Ollama is down (#117).
/// </summary>
public class JdHeuristicsTests
{
    private static readonly string[] Concepts =
    {
        "Arrays", "Strings", "Time Complexity", "Binary Trees", "Merge Sort",
        "Knapsack Problem", "Weighted Graphs", "Hash Maps and Sets"
    };

    private const string SampleJd =
        "Amazon is hiring an SDE-1. Strong fundamentals in DSA, proficiency in " +
        "Java/C++/Python. Experience with Arrays, Strings, and Binary Trees " +
        "required. Knowledge of Big O notation and Merge Sort is a plus. " +
        "Own deliverables end to end in an agile team environment.";

    [Fact]
    public void MatchSkills_ConceptNamesFound_WordBoundaryCaseInsensitive()
    {
        var result = JdHeuristics.MatchSkills(SampleJd, Concepts);

        Assert.Contains("Arrays", result.Keys);
        Assert.Contains("Strings", result.Keys);
        Assert.Contains("Binary Trees", result.Keys);
        Assert.Contains("Merge Sort", result.Keys);
        // "Big O notation" is not a literal concept name, but the alias scan
        // maps it to "Time Complexity" when that concept exists (#117 done-when:
        // the API.md sample's "Big O"/"DSA" phrasing must yield skills).
        Assert.Contains("Time Complexity", result.Keys);
    }

    [Fact]
    public void MatchSkills_AliasPhrases_MapToTargetConcept()
    {
        // The verbatim API.md §3 sample relies on "DSA" — no literal concept name.
        var result = JdHeuristics.MatchSkills(
            "Amazon is hiring an SDE-1. Strong fundamentals in DSA, proficiency in Java/C++/Python...",
            Concepts);

        Assert.Contains("Arrays", result.Keys); // "dsa" alias → "Arrays"
        Assert.Equal(1f, result.Values.Sum(), 5);
    }

    [Fact]
    public void MatchSkills_ConceptEndingInParenthesis_Matches()
    {
        // \b after ')' can never match — seeded "…(BFS)"/"…(DFS)" names were
        // unmatchable until the lookaround fix (#117 review).
        var concepts = new[] { "Breadth-First Search (BFS)", "Arrays" };
        var result = JdHeuristics.MatchSkills(
            "Experience with Breadth-First Search (BFS) is required.", concepts);

        Assert.Contains("Breadth-First Search (BFS)", result.Keys);
    }

    [Fact]
    public void MatchSkills_NoMatches_ReturnsEmpty()
    {
        var result = JdHeuristics.MatchSkills("We need a nurse for our clinic.", Concepts);

        Assert.Empty(result);
    }

    [Fact]
    public void MatchSkills_UniformWeightsSumToOne()
    {
        var result = JdHeuristics.MatchSkills(SampleJd, Concepts);

        Assert.Equal(1f, result.Values.Sum(), 5);
    }

    [Fact]
    public void MatchSkills_SubstringDoesNotLeakAcrossWords()
    {
        // "bowstrings" contains "strings" but has no word boundary before it;
        // "arraysx" has no boundary after "arrays"
        var result = JdHeuristics.MatchSkills("The bowstrings arraysx are noted.", Concepts);

        Assert.Empty(result);
    }

    [Fact]
    public void Canonicalize_ExactMatch_ReturnsConcept()
    {
        Assert.Equal("Arrays", JdHeuristics.Canonicalize("Arrays", Concepts));
    }

    [Fact]
    public void Canonicalize_CaseInsensitiveMatch_ReturnsDbCasing()
    {
        Assert.Equal("Arrays", JdHeuristics.Canonicalize("arrays", Concepts));
        Assert.Equal("Binary Trees", JdHeuristics.Canonicalize("binary trees", Concepts));
    }

    [Fact]
    public void Canonicalize_Alias_MapsToConceptWhenPresent()
    {
        Assert.Equal("Knapsack Problem", JdHeuristics.Canonicalize("Dynamic Programming", Concepts));
        Assert.Equal("Time Complexity", JdHeuristics.Canonicalize("Big O", Concepts));
    }

    [Fact]
    public void Canonicalize_AliasTargetMissing_KeepsRawName()
    {
        // Alias target "Priority Queues" is not in the vocabulary → pass through.
        Assert.Equal("Priority Scheduling", JdHeuristics.Canonicalize("Priority Scheduling", Concepts));
    }

    [Fact]
    public void Canonicalize_UnknownSkill_KeepsTrimmedRawName()
    {
        Assert.Equal("Kubernetes", JdHeuristics.Canonicalize("  Kubernetes  ", Concepts));
    }

    [Fact]
    public void Parse_SampleAmazonJd_ExtractsCompanyRoleAndSkills()
    {
        var result = JdHeuristics.Parse(SampleJd, Concepts);

        Assert.Equal("Amazon", result.CompanyName);
        Assert.Equal("SDE-1", result.Role);
        Assert.NotEmpty(result.TechnicalSkills);
        Assert.Empty(result.HrSkills); // heuristic finds HR skills only via LLM
        Assert.Equal("Medium", result.Difficulty);
        Assert.Equal(6, result.EstimatedWeeks);
    }

    [Fact]
    public void Parse_AboutAndJoinHeadings_ExtractCompany()
    {
        // Scoped (?i:…) keywords: the doc-comment forms "About X"/"Join X" must
        // match without letting lowercase noise words become the company.
        Assert.Equal("Amazon", JdHeuristics.Parse(
            "About Amazon. We build customer-obsessed teams.", Concepts).CompanyName);
        Assert.Equal("Microsoft Corp", JdHeuristics.Parse(
            "Join Microsoft Corp today and grow.", Concepts).CompanyName);
        Assert.Equal("Unknown", JdHeuristics.Parse(
            "We are hiring for our clinic.", Concepts).CompanyName);
    }

    [Fact]
    public void Parse_OversizedCompanyToken_FallsBackToUnknown()
    {
        // A 250-char "word" after hiring must not overflow varchar(200) — the
        // bounded token + \b makes the whole match fail → "Unknown" (#117 review).
        var result = JdHeuristics.Parse(
            "we are hiring " + new string('A', 250), Concepts);

        Assert.Equal("Unknown", result.CompanyName);
    }

    [Fact]
    public void Parse_UnmatchedText_FallsBackToDefaults()
    {
        var result = JdHeuristics.Parse(
            "zzz qqq xxx yyy www vvv uuu ttt sss rrr ppp ooo nnn mmm lll kkk",
            Concepts);

        Assert.Equal("Unknown", result.CompanyName);
        Assert.Equal("Unknown", result.Role);
        Assert.Empty(result.TechnicalSkills);
        Assert.Equal("Medium", result.Difficulty);
        Assert.Equal(6, result.EstimatedWeeks);
    }
}
