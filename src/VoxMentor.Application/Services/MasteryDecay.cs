namespace VoxMentor.Application.Services;

/// <summary>
/// Spaced-repetition decay (#57): a mastery row idle past the grace window
/// loses 5% of its remaining probability per idle day, down to a 0.1 floor.
/// <see cref="Domain.Entities.StudentMastery.UpdatedAt"/> is the watermark:
/// the job bumps it when it applies a decay, so the next decay event waits
/// another grace window instead of re-compounding the same gap nightly.
/// </summary>
public static class MasteryDecay
{
    public const double DailyFactor = 0.95;
    public const double Floor = 0.1;
    public const int GraceDays = 7;

    /// <summary>
    /// New mastery value, or null when the row is not due (fewer than
    /// <c>GraceDays + 1</c> full days since <paramref name="updatedAt"/>).
    /// </summary>
    public static double? Compute(double mastery, DateTime updatedAt, DateTime now)
    {
        var idleDays = (int)Math.Floor((now - updatedAt).TotalDays) - GraceDays;
        if (idleDays < 1)
        {
            return null;
        }

        return Math.Max(Floor, mastery * Math.Pow(DailyFactor, idleDays));
    }
}
