using VoxMentor.Application.Features.Practice.GetReadiness;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Services;

/// <summary>
/// JD-weighted readiness math shared by every readiness call site
/// (GET practice/readiness #37, GET /jd/{jdId} #118, mock-interview complete #125).
/// Pure function over already-loaded, read-only data — no EF, no tracking.
/// Keeping the formula in one place is what stops the scores from drifting
/// between those endpoints.
/// </summary>
public static class ReadinessCalculator
{
    /// <summary>
    /// Weighted readiness score (0–100), per-skill breakdown, and gap list
    /// ordered by severity descending.
    /// </summary>
    /// <param name="skillWeights">JD skill rows (typically AsNoTracking).</param>
    /// <param name="concepts">All concepts; matched to weights by name (first wins).</param>
    /// <param name="masteries">User's mastery rows keyed by concept id.</param>
    /// <param name="estimatedWeeks">The JD's own estimate, passed through untouched.</param>
    public static ReadinessDto Compute(
        IReadOnlyList<JdSkillWeight> skillWeights,
        IReadOnlyList<Concept> concepts,
        IReadOnlyDictionary<Guid, StudentMastery> masteries,
        int estimatedWeeks)
    {
        var conceptByName = concepts
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First());

        var breakdown = new List<SkillBreakdownDto>();
        float scoreSum = 0f;

        foreach (var sw in skillWeights)
        {
            if (!conceptByName.TryGetValue(sw.SkillName, out var concept))
                continue;

            masteries.TryGetValue(concept.Id, out var mastery);
            var masteryValue = mastery?.MasteryProbability ?? 0f;
            var contribution = masteryValue * sw.Weight;
            scoreSum += contribution;

            breakdown.Add(new SkillBreakdownDto
            {
                Topic = sw.SkillName,
                Mastery = masteryValue,
                JdWeight = sw.Weight,
                Contribution = contribution
            });
        }

        var gaps = breakdown
            .Where(b => b.Mastery < 1f)
            .Select(b => new SkillGapDto
            {
                Topic = b.Topic,
                Severity = b.JdWeight * (1f - b.Mastery),
                Recommendation = $"Practice {b.Topic} — {b.JdWeight * (1f - b.Mastery):P0} gap remaining."
            })
            .OrderByDescending(g => g.Severity)
            .ToList();

        return new ReadinessDto
        {
            Score = skillWeights.Count == 0 ? 0f : scoreSum * 100f,
            Breakdown = breakdown,
            Gaps = gaps,
            EstimatedWeeksToReady = estimatedWeeks
        };
    }
}
