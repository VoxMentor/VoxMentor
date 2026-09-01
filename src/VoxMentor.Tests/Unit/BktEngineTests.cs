using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;
using Xunit;

namespace VoxMentor.Tests.Unit;

public class BktEngineTests
{
    private readonly BktEngine _engine = new();

    private readonly BktParameters _defaultParams = new()
    {
        PriorKnowledge = 0.1f,
        LearnRate = 0.3f,
        GuessRate = 0.2f,
        SlipRate = 0.1f
    };

    [Fact]
    public void UpdateMastery_CorrectAnswer_IncreasesMastery()
    {
        float result = _engine.UpdateMastery(0.1f, _defaultParams, correct: true);
        Assert.True(result > 0.1f);
    }

    [Fact]
    public void UpdateMastery_IncorrectAnswer_DecreasesMastery()
    {
        float result = _engine.UpdateMastery(0.5f, _defaultParams, correct: false);
        Assert.True(result < 0.5f);
    }

    [Fact]
    public void UpdateMastery_MasteryNeverExceedsOne()
    {
        float result = _engine.UpdateMastery(0.9f, _defaultParams, correct: true);
        Assert.True(result <= 1.0f);
    }

    [Fact]
    public void UpdateMastery_MasteryNeverDropsBelowZero()
    {
        float result = _engine.UpdateMastery(0.01f, _defaultParams, correct: false);
        Assert.True(result >= 0.0f);
    }

    [Fact]
    public void UpdateMastery_MultipleCorrect_HighMastery()
    {
        float mastery = 0.1f;
        for (int i = 0; i < 10; i++)
            mastery = _engine.UpdateMastery(mastery, _defaultParams, correct: true);
        Assert.True(mastery > 0.9f);
    }

    [Fact]
    public void UpdateMastery_BatchUpdate_SameAsIndividualCalls()
    {
        var observations = new[] { true, true, false, true, false, true };
        float batchResult = _engine.UpdateMastery(0.1f, _defaultParams, observations);

        float individualResult = 0.1f;
        foreach (var obs in observations)
            individualResult = _engine.UpdateMastery(individualResult, _defaultParams, obs);

        Assert.Equal(batchResult, individualResult);
    }
}
