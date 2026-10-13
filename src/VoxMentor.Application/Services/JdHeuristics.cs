using System.Text.RegularExpressions;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Services;

/// <summary>
/// Keyword-heuristic JD parsing and skill-name canonicalization (#117).
/// Used when Ollama is down or returns garbage, and to map LLM-emitted skill
/// names onto seeded concept names so <see cref="ReadinessCalculator"/>'s
/// ordinal name matching does not silently drop the rows (#116 review).
/// Pure static — the caller passes the DB concept vocabulary in.
/// </summary>
public static partial class JdHeuristics
{
    /// <summary>Company fallback when no pattern matches.</summary>
    public const string UnknownCompany = "Unknown";

    /// <summary>Role fallback when no pattern matches.</summary>
    public const string UnknownRole = "Unknown";

    /// <summary>Difficulty fallback (no signal).</summary>
    public const string DefaultDifficulty = "Medium";

    /// <summary>Estimated weeks fallback — mirrors the sample JD's shape.</summary>
    public const int DefaultEstimatedWeeks = 6;

    /// <summary>
    /// Common JD phrasing → concept-name aliases. Applied only when the alias
    /// target exists in the caller's concept vocabulary; keys are lowercase.
    /// ponytail: short hand-picked map, extend as real JDs surface new phrasing.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dsa"] = "Arrays",
        ["data structures and algorithms"] = "Arrays",
        ["data structures"] = "Arrays",
        ["algorithms"] = "Time Complexity",
        ["big-o"] = "Time Complexity",
        ["big o"] = "Time Complexity",
        ["big o notation"] = "Time Complexity",
        ["dynamic programming"] = "Knapsack Problem",
        ["dp"] = "Knapsack Problem",
        ["graphs"] = "Weighted Graphs",
        ["graph algorithms"] = "Weighted Graphs",
        ["trees"] = "Binary Trees",
        ["binary trees"] = "Binary Trees",
        ["hashmaps"] = "Hash Maps and Sets",
        ["hash maps"] = "Hash Maps and Sets",
        ["hash tables"] = "Hash Maps and Sets",
        ["heaps"] = "Heap Data Structure",
        ["tries"] = "Trie",
        ["system design"] = "Time Complexity",
        ["oop"] = "Functions",
        ["object oriented programming"] = "Functions",
        ["recursion"] = "Recursion",
        ["stack"] = "Stacks",
        ["queues"] = "Queues",
        ["sorting"] = "Merge Sort",
        ["searching"] = "Binary Search",
    };

    /// <summary>
    /// Maps a skill name onto the concept vocabulary: exact match, then
    /// case-insensitive match, then alias. Unmatched names pass through
    /// unchanged (novel skills are kept, not dropped).
    /// </summary>
    public static string Canonicalize(string skillName, IReadOnlyCollection<string> conceptNames)
    {
        var trimmed = skillName.Trim();
        if (trimmed.Length == 0)
            return trimmed;

        foreach (var concept in conceptNames)
        {
            if (string.Equals(concept, trimmed, StringComparison.Ordinal))
                return concept;
        }

        foreach (var concept in conceptNames)
        {
            if (string.Equals(concept, trimmed, StringComparison.OrdinalIgnoreCase))
                return concept;
        }

        if (Aliases.TryGetValue(trimmed, out var alias) &&
            conceptNames.Any(c => string.Equals(c, alias, StringComparison.OrdinalIgnoreCase)))
        {
            return conceptNames.First(c => string.Equals(c, alias, StringComparison.OrdinalIgnoreCase));
        }

        return trimmed;
    }

    /// <summary>
    /// Scans raw JD text for concept names (case-insensitive, word-boundary)
    /// plus alias phrasing ("DSA", "Big O", …) mapped to their target concept,
    /// and returns uniform weights across matches. Zero matches → empty map.
    /// </summary>
    public static Dictionary<string, float> MatchSkills(string rawText, IReadOnlyCollection<string> conceptNames)
    {
        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var concept in conceptNames)
        {
            if (ConceptNamePattern(concept).IsMatch(rawText))
                matched.Add(concept);
        }

        // Real JDs say "DSA"/"Big O"/"DP" far more often than seed concept names —
        // scan the alias vocabulary too and count the TARGET concept (done-when:
        // the API.md sample JD finds skills on the heuristic path, not just via LLM).
        foreach (var (phrase, target) in Aliases)
        {
            var concept = conceptNames.FirstOrDefault(c =>
                string.Equals(c, target, StringComparison.OrdinalIgnoreCase));
            if (concept is not null && ConceptNamePattern(phrase).IsMatch(rawText))
                matched.Add(concept);
        }

        if (matched.Count == 0)
            return new Dictionary<string, float>(StringComparer.Ordinal);

        var uniform = 1f / matched.Count;
        return matched.ToDictionary(c => c, _ => uniform, StringComparer.Ordinal);
    }

    /// <summary>
    /// Full heuristic parse: uniform concept-name skill weights, light
    /// company/role regexes, fixed difficulty/weeks defaults.
    /// </summary>
    public static JdParseResult Parse(string rawText, IReadOnlyCollection<string> conceptNames)
    {
        var company = CompanyPattern().Match(rawText);
        var role = RolePattern().Match(rawText);

        return new JdParseResult
        {
            CompanyName = company.Success ? company.Groups[1].Value.Trim() : UnknownCompany,
            Role = role.Success ? role.Groups[1].Value.Trim() : UnknownRole,
            TechnicalSkills = MatchSkills(rawText, conceptNames),
            HrSkills = new Dictionary<string, float>(StringComparer.Ordinal),
            Difficulty = DefaultDifficulty,
            EstimatedWeeks = DefaultEstimatedWeeks
        };
    }

    /// <summary>
    /// "Amazon is hiring" / "About Amazon" / "Join Microsoft Corp" → company.
    /// Keywords are case-insensitive via scoped (?i:…) while the name keeps its
    /// capitalized-word shape ("We are hiring…" → Unknown, not company "We").
    /// Tokens are capped at 40 chars so a 250-char "word" fails \b and falls
    /// back to "Unknown" instead of overflowing varchar(200); continuation
    /// words stop after a sentence-ending period ("About Amazon. We build…"
    /// yields "Amazon", not "Amazon. We").
    /// </summary>
    [GeneratedRegex(@"\b(?<name>[A-Z][\w&.\-']{0,39}(?:\s(?<!\.\s)[A-Z][\w&.\-']{0,39}){0,3})\s+(?:(?i:is)\s+)?(?i:hiring|seeking|looking)\b|\b(?i:hiring|about|join(?:ing)?)\s+(?<name>[A-Z][\w&.\-']{0,39}(?:\s(?<!\.\s)[A-Z][\w&.\-']{0,39}){0,2})\b")]
    private static partial Regex CompanyPattern();

    /// <summary>Common SDE-style titles → role.</summary>
    [GeneratedRegex(@"\b((?:Sr\.?\s+|Senior\s+|Junior\s+)?(?:SDE|SWE|Software Engineer|Backend Engineer|Frontend Engineer|Full[\s-]?Stack Engineer|Data Scientist|ML Engineer|DevOps Engineer|QA Engineer)(?:\s*[-–]?\s*(?:I{1,3}|[1-9]))?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex RolePattern();

    /// <summary>
    /// Word-boundary concept match via lookarounds, NOT \b: seeded names like
    /// "Breadth-First Search (BFS)" end in ')', where \b can never match
    /// between two non-word chars, so the concept was unmatchable. Escaped so
    /// "C++"-style names stay safe.
    /// </summary>
    private static Regex ConceptNamePattern(string conceptName) =>
        ConceptRegexCache.GetOrAdd(conceptName, static name => new Regex(
            $@"(?<!\w){Regex.Escape(name)}(?!\w)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> ConceptRegexCache = new();
}
