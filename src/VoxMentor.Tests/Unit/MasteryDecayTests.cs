using VoxMentor.Application.Services;
using Xunit;

namespace VoxMentor.Tests.Unit;

public class MasteryDecayTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Compute_WithinGraceWindow_ReturnsNull()
    {
        var anchor = Now.AddDays(-3);
        Assert.Null(MasteryDecay.Compute(0.8, anchor, anchor, Now));
        Assert.Null(MasteryDecay.Compute(0.8, Now.AddDays(-7), Now.AddDays(-7), Now));
        Assert.Null(MasteryDecay.Compute(0.8, Now.AddDays(-7.9), Now.AddDays(-7.9), Now));
    }

    [Fact]
    public void Compute_EightDaysIdle_DecaysOneDay()
    {
        var anchor = Now.AddDays(-8);

        var result = MasteryDecay.Compute(0.8, anchor, anchor, Now);

        Assert.NotNull(result);
        Assert.Equal(0.8 * 0.95, result!.Value, 10);
    }

    [Fact]
    public void Compute_ThirtyDaysIdle_NeverDecayed_CompoundsFullGap()
    {
        var anchor = Now.AddDays(-30);

        var result = MasteryDecay.Compute(1.0, anchor, anchor, Now);

        Assert.NotNull(result);
        // idleDays = floor(30) - 7 = 23.
        Assert.Equal(Math.Pow(0.95, 23), result!.Value, 10);
    }

    [Fact]
    public void Compute_AfterPriorDecay_SkipsAlreadyAppliedDays()
    {
        var anchor = Now.AddDays(-30);
        var day8 = anchor.AddDays(8);

        var firstRun = MasteryDecay.Compute(1.0, anchor, anchor, day8);
        Assert.Equal(0.95, firstRun!.Value, 10); // idleDays = 1

        var secondRun = MasteryDecay.Compute(firstRun.Value, anchor, day8, Now);

        // Regression for the watermark bug: the second run applies the 22 days
        // still outstanding (23 total - 1 already applied), so the value after
        // two runs is 0.95^23 — not 0.95^3 as when the watermark reset the
        // idle window on every decay write.
        Assert.Equal(Math.Pow(0.95, 23), secondRun!.Value, 10);
    }

    [Fact]
    public void Compute_SameDaySecondRun_NoFurtherDecay()
    {
        var anchor = Now.AddDays(-10);
        var firstRun = MasteryDecay.Compute(0.8, anchor, anchor, Now);
        Assert.NotNull(firstRun);

        // Job bumps UpdatedAt to now; an immediate re-run must be a no-op.
        Assert.Null(MasteryDecay.Compute(firstRun!.Value, anchor, Now, Now.AddMinutes(5)));
    }

    [Fact]
    public void Compute_LongIdle_ClampsToFloor()
    {
        var anchor = Now.AddDays(-300);

        var result = MasteryDecay.Compute(0.8, anchor, anchor, Now);

        Assert.Equal(MasteryDecay.Floor, result!.Value, 10);
    }

    [Fact]
    public void Compute_AlreadyAtFloor_StaysAtFloor()
    {
        var anchor = Now.AddDays(-10);

        var result = MasteryDecay.Compute(MasteryDecay.Floor, anchor, anchor, Now);

        Assert.Equal(MasteryDecay.Floor, result!.Value, 10);
    }

    [Fact]
    public void Compute_FutureTimestamp_NoDecay()
    {
        var anchor = Now.AddDays(1);
        Assert.Null(MasteryDecay.Compute(0.8, anchor, anchor, Now));
    }
}
