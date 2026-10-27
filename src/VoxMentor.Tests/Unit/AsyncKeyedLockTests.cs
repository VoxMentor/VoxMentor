using VoxMentor.Application.Common.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="AsyncKeyedLock"/>: same-key acquirers serialize,
/// different keys do not, double release is safe (#124).
/// </summary>
public class AsyncKeyedLockTests
{
    [Fact]
    public async Task SameKey_SecondAcquire_WaitsUntilFirstReleased()
    {
        var l = new AsyncKeyedLock();
        using var first = await l.AcquireAsync("k");

        var second = l.AcquireAsync("k");
        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        first.Dispose();
        using var handle = await second;
        Assert.True(second.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DifferentKeys_DoNotBlockEachOther()
    {
        var l = new AsyncKeyedLock();
        using var a = await l.AcquireAsync("a");
        var b = l.AcquireAsync("b");

        Assert.True(b.IsCompletedSuccessfully);
        b.Dispose();
    }

    [Fact]
    public async Task DoubleDispose_ReleasesOnlyOnce()
    {
        var l = new AsyncKeyedLock();
        var first = await l.AcquireAsync("k");
        first.Dispose();
        first.Dispose(); // must not over-release the semaphore

        var again = await l.AcquireAsync("k");
        var contender = l.AcquireAsync("k");
        await Task.Delay(50);
        Assert.False(contender.IsCompleted); // proves count is still 1, not 2

        again.Dispose();
        using var got = await contender; // completes on the one legitimate release
    }
}
