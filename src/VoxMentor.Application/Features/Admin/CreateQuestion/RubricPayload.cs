using System.Text.Json;

namespace VoxMentor.Application.Features.Admin.CreateQuestion;

/// <summary>
/// Parses and validates rubric payloads. Each entry is a JSON object with a
/// nonempty string "criterion" and a numeric "points" field, e.g.
/// {"criterion":"Handles edge cases","points":2}.
/// </summary>
public static class RubricPayload
{
    /// <param name="Errors">Per-entry validation errors, empty when valid.</param>
    public sealed record Result(IReadOnlyList<string> Errors)
    {
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Validates every entry. A null array is treated as empty (no rubric is
    /// optional); a null element, a non-object entry, or a missing/empty
    /// "criterion" or non-numeric "points" produces an error.
    /// </summary>
    public static Result Parse(string[]? rubric)
    {
        var errors = new List<string>();
        if (rubric is null)
            return new Result(errors);

        for (var i = 0; i < rubric.Length; i++)
        {
            if (rubric[i] is null)
            {
                errors.Add($"Rubric entry {i} must not be null.");
                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(rubric[i]);
            }
            catch (JsonException)
            {
                errors.Add($"Rubric entry {i} is not valid JSON.");
                continue;
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind is not JsonValueKind.Object)
                {
                    errors.Add($"Rubric entry {i} must be a JSON object.");
                    continue;
                }

                if (!doc.RootElement.TryGetProperty("criterion", out var criterion)
                    || criterion.ValueKind is not JsonValueKind.String
                    || string.IsNullOrWhiteSpace(criterion.GetString()))
                    errors.Add($"Rubric entry {i} is missing a nonempty string \"criterion\" field.");

                if (!doc.RootElement.TryGetProperty("points", out var points)
                    || points.ValueKind is not (JsonValueKind.Number))
                    errors.Add($"Rubric entry {i} is missing a numeric \"points\" field.");
            }
        }

        return new Result(errors);
    }
}
