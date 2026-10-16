using VoxMentor.Application.Common;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Tests for <see cref="IsomorphicInstance"/> (#120): deterministic per-student
/// question instance ids.
/// </summary>
public class IsomorphicInstanceTests
{
    [Fact]
    public void Id_SameUserQuestionSeed_ReturnsSameIdTwice()
    {
        var userId = "user-1";
        var questionId = Guid.NewGuid();

        var first = IsomorphicInstance.Id(userId, questionId, "seed-a");
        var second = IsomorphicInstance.Id(userId, questionId, "seed-a");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Id_DifferentUsers_ReturnDifferentIds()
    {
        var questionId = Guid.NewGuid();

        var first = IsomorphicInstance.Id("user-1", questionId, "seed-a");
        var second = IsomorphicInstance.Id("user-2", questionId, "seed-a");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Id_NullSeed_StillReturnsId()
    {
        var questionId = Guid.NewGuid();

        var id = IsomorphicInstance.Id("user-1", questionId, null);
        var again = IsomorphicInstance.Id("user-1", questionId, null);

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(id, again);
    }

    [Fact]
    public void Id_DifferentSeeds_ReturnDifferentIds()
    {
        var questionId = Guid.NewGuid();

        var first = IsomorphicInstance.Id("user-1", questionId, "seed-a");
        var second = IsomorphicInstance.Id("user-1", questionId, "seed-b");

        Assert.NotEqual(first, second);
    }
}
