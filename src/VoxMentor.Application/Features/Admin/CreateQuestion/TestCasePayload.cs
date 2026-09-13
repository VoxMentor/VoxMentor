using System.Text.Json;

namespace VoxMentor.Application.Features.Admin.CreateQuestion;

/// <summary>
/// Parses and validates question test-case payloads. Each entry is a JSON object
/// with required string "input" and "expected" fields and an optional boolean
/// "hidden" flag. Hidden cases must be trailing (see SubmitCodeHandler, which
/// hides the last HiddenTestCaseCount cases).
/// </summary>
public static class TestCasePayload
{
    /// <summary>Result of parsing a test-case array.</summary>
    /// <param name="HiddenCount">Number of trailing hidden cases.</param>
    /// <param name="Errors">Per-entry validation errors, empty when valid.</param>
    public sealed record Result(int HiddenCount, IReadOnlyList<string> Errors);

    /// <summary>
    /// Validates every entry and counts trailing hidden cases. An entry that is
    /// not a JSON object, or missing/typing-wrong "input"/"expected"/"hidden",
    /// produces an error. A non-hidden case after a hidden one produces an error.
    /// </summary>
    public static Result Parse(string[] testCases)
    {
        var errors = new List<string>();
        var hiddenCount = 0;
        var seenHidden = false;

        for (var i = 0; i < testCases.Length; i++)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(testCases[i]);
            }
            catch (JsonException)
            {
                errors.Add($"Test case {i} is not valid JSON.");
                continue;
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind is not JsonValueKind.Object)
                {
                    errors.Add($"Test case {i} must be a JSON object.");
                    continue;
                }

                var input = doc.RootElement.TryGetProperty("input", out var inputValue)
                    && inputValue.ValueKind is JsonValueKind.String;
                if (!input)
                    errors.Add($"Test case {i} is missing a string \"input\" field.");

                var expected = doc.RootElement.TryGetProperty("expected", out var expectedValue)
                    && expectedValue.ValueKind is JsonValueKind.String;
                if (!expected)
                    errors.Add($"Test case {i} is missing a string \"expected\" field.");

                var hidden = false;
                if (doc.RootElement.TryGetProperty("hidden", out var hiddenValue))
                {
                    if (hiddenValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        hidden = hiddenValue.GetBoolean();
                    }
                    else
                    {
                        errors.Add($"Test case {i} \"hidden\" must be a boolean.");
                    }
                }

                if (hidden)
                {
                    hiddenCount++;
                    seenHidden = true;
                }
                else if (seenHidden)
                {
                    errors.Add($"Test case {i} is not hidden but follows a hidden case; hidden cases must be trailing.");
                }
            }
        }

        return new Result(hiddenCount, errors);
    }
}
