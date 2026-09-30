namespace VoxMentor.Application.Services;

/// <summary>
/// Spaced-repetition decay (#57): a mastery row idle past the grace window
/// loses 5% of its remaining probability per idle day, down to a 0.1 floor.
/// Idle time is measured from the last practice (falling back to creation),
/// never from a decay write, so decay cannot reset the idle window.
/// <see cref="Domain.Entities.StudentMastery.UpdatedAt"/> is the last-write
/// watermark: it records how many idle days were already applied, which is
/// what makes successive runs telescope to 0.95^totalIdleDays.
/// </summary>
public static class MasteryDecay
{
    public const double DailyFactor = 0.95;
    public const double Floor = 0.1;
    public const int GraceDays = 7;

    /// <summary>
    /// New mastery value, or null when no new idle day is due (fewer than
    /// <c>GraceDays + 1</c> full days since <paramref name="lastPracticedAt"/>,
    /// or those days were already applied).
    /// </summary>
    /// <param name="mastery">Current mastery probability.</param>
    /// <param name="lastPracticedAt">Idle anchor: time of last practice, or creation when never practiced.</param>
    /// <param name="lastWrittenAt">Last write to the row (practice or decay) — how far the applied decay has already run.</param>
    /// <param name="now">Current time (UTC).</param>
    public static double? Compute(double mastery, DateTime lastPracticedAt, DateTime lastWrittenAt, DateTime now)
    {
        var totalIdle = (int)Math.Floor((now - lastPracticedAt).TotalDays) - GraceDays;
        var alreadyApplied = Math.Max(
            0,
            (int)Math.Floor((lastWrittenAt - lastPracticedAt).TotalDays) - GraceDays);
        var idleDays = totalIdle - alreadyApplied;
        if (idleDays < 1)
        {
            return null;
        }

        return Math.Max(Floor, mastery * Math.Pow(DailyFactor, idleDays));
    }
}
