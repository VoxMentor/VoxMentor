using VoxMentor.Application.Features.Jd.GetJd;
using VoxMentor.Application.Features.Practice.GetReadiness;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Services;

/// <summary>
/// Buckets severity-ranked gaps into a week-by-week study roadmap (#118).
/// Pure function over already-loaded, read-only data — no EF, no tracking.
/// Week 1 always holds the highest-severity topic.
/// </summary>
public static class RoadmapBuilder
{
    /// <summary>
    /// Chunks <paramref name="gaps"/> (expected severity-descending) into
    /// <c>estimatedWeeks</c> buckets. When there are more topics than weeks,
    /// several topics share a week so none is dropped; when there are fewer,
    /// trailing empty weeks are omitted.
    /// </summary>
    /// <param name="gaps">Gaps ordered by severity descending.</param>
    /// <param name="concepts">All concepts; a topic resolves to the first concept sharing its name (first wins), matching <see cref="ReadinessCalculator"/>.</param>
    /// <param name="estimatedWeeks">The JD's own estimate.</param>
    public static List<RoadmapWeekDto> Build(
        IReadOnlyList<SkillGapDto> gaps,
        IReadOnlyList<Concept> concepts,
        int estimatedWeeks)
    {
        var roadmap = new List<RoadmapWeekDto>();
        if (gaps.Count == 0 || estimatedWeeks < 1)
            return roadmap;

        // First-wins per name, same as ReadinessCalculator: Concept.Name is not
        // unique, and emitting every same-named row would point a student at a
        // second concept whose mastery the score never counted.
        var conceptIdByName = concepts
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var perWeek = (int)Math.Ceiling(gaps.Count / (double)estimatedWeeks);
        var weeks = (int)Math.Ceiling(gaps.Count / (double)perWeek);

        for (var w = 0; w < weeks; w++)
        {
            var focusTopics = gaps
                .Skip(w * perWeek)
                .Take(perWeek)
                .Select(g => g.Topic)
                .ToList();

            roadmap.Add(new RoadmapWeekDto
            {
                Week = w + 1,
                Focus = string.Join(", ", focusTopics),
                ConceptIds = focusTopics
                    .Where(conceptIdByName.ContainsKey)
                    .Select(topic => conceptIdByName[topic])
                    .ToList()
            });
        }

        return roadmap;
    }
}
