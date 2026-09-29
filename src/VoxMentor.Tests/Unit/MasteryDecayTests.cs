using VoxMentor.Application.Services;
using Xunit;

namespace VoxMentor.Tests.Unit;

public class MasteryDecayTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Compute_WithinGraceWindow_ReturnsNull()
    {
        Assert.Null(MasteryDecay.Compute(0.8, Now.AddDays(-3), Now));
        Assert.Null(MasteryDecay.Compute(0.8, Now.AddDays(-7), Now));
        Assert.Null(MasteryDecay.Compute(0.8, Now.AddDays(-7.9), Now));
    }

    [Fact]
    public void Compute_EightDaysIdle_DecaysOneDay()
    {
        var result = MasteryDecay.Compute(0.8, Now.AddDays(-8), Now);

        Assert.NotNull(result);
        Assert.Equal(0.8 * 0.95, result!.Value, 10);
    }

    [Fact]
    public void Compute_ThirtyDaysIdle_CompoundsSinceLastWrite()
    {
        var result = MasteryDecay.Compute(1.0, Now.AddDays(-30), Now);

        Assert.NotNull(result);
        // idleDays = floor(30) - 7 = 23.
        Assert.Equal(Math.Pow(0.95, 23), result!.Value, 10);
    }

    [Fact]
    public void Compute_LongIdle_ClampsToFloor()
    {
        var result = MasteryDecay.Compute(0.8, Now.AddDays(-300), Now);

        Assert.Equal(MasteryDecay.Floor, result!.Value, 10);
    }

    [Fact]
    public void Compute_AlreadyAtFloor_StaysAtFloor()
    {
        var result = MasteryDecay.Compute(MasteryDecay.Floor, Now.AddDays(-10), Now);

        Assert.Equal(MasteryDecay.Floor, result!.Value, 10);
    }

    [Fact]
    public void Compute_FutureTimestamp_NoDecay()
    {
        Assert.Null(MasteryDecay.Compute(0.8, Now.AddDays(1), Now));
    }
}
