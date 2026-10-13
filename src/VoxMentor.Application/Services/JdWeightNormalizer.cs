namespace VoxMentor.Application.Services;

/// <summary>
/// Weight hygiene for JD skill maps (#117): never trust the LLM's arithmetic.
/// Drops non-positive and non-finite weights, merges duplicate keys, and
/// renormalizes the survivors so each map sums to exactly 1.0. An all-dropped
/// map returns empty — no divide-by-zero, no invented skills.
/// </summary>
public static class JdWeightNormalizer
{
    /// <summary>
    /// Normalized copy of <paramref name="weights"/> summing to 1.0,
    /// or an empty dictionary when nothing survives the filter.
    /// </summary>
    public static Dictionary<string, float> Normalize(IReadOnlyDictionary<string, float> weights)
    {
        // ponytail: merge-after-trim because canonicalized keys can collide
        // ("Arrays " and "Arrays" → one row; the DB unique index would reject the pair).
        var clean = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (skill, weight) in weights)
        {
            if (string.IsNullOrWhiteSpace(skill) || !float.IsFinite(weight) || weight <= 0f)
                continue;

            var key = skill.Trim();
            clean.TryGetValue(key, out var existing);
            clean[key] = existing + weight;
        }

        var sum = clean.Values.Sum();
        if (!float.IsFinite(sum) || sum <= 0f)
        {
            // Overflow (huge finite weights summed to +Inf) or degenerate —
            // uniform weights keep the "sums to 1.0" contract honest.
            var uniform = 1f / clean.Count;
            return clean.Keys.ToDictionary(k => k, _ => uniform, StringComparer.Ordinal);
        }

        return clean.ToDictionary(
            kv => kv.Key,
            kv => kv.Value / sum,
            StringComparer.Ordinal);
    }
}
