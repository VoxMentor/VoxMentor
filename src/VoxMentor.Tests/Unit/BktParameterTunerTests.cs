using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;
using Xunit;

namespace VoxMentor.Tests.Unit;

public class BktParameterTunerTests
{
    private const double Prior = 0.2;
    private const double Slip = 0.1;
    private const double Guess = 0.2;
    private const double Learn = 0.3;

    [Fact]
    public void Tune_EmptySequences_KeepsCurrentParameters()
    {
        var current = new BktParameters { PriorKnowledge = (float)Prior };

        var result = BktParameterTuner.Tune([], current);

        Assert.Equal(Slip, result.Slip, 6);
        Assert.Equal(Guess, result.Guess, 6);
        Assert.Equal(Learn, result.Learn, 6);
        Assert.Equal(0, result.LogLikelihood, 6);
        Assert.True(result.Iterations >= 1 && result.Iterations <= BktParameterTuner.MaxIterations);
    }

    [Fact]
    public void Tune_SyntheticSequences_ImprovesLikelihoodAndStaysClamped()
    {
        var sequences = Synthesize(sequenceCount: 60, length: 20);
        var current = new BktParameters { PriorKnowledge = (float)Prior };
        var initialLl = BktParameterTuner.LogLikelihood(
            sequences, current.SlipRate, current.GuessRate, current.LearnRate, current.PriorKnowledge);

        var result = BktParameterTuner.Tune(sequences, current);

        Assert.True(result.LogLikelihood >= initialLl, $"EM regressed: {initialLl} -> {result.LogLikelihood}");
        Assert.InRange(result.Slip, BktParameterTuner.MinProbability, BktParameterTuner.MaxProbability);
        Assert.InRange(result.Guess, BktParameterTuner.MinProbability, BktParameterTuner.MaxProbability);
        Assert.InRange(result.Learn, BktParameterTuner.MinProbability, BktParameterTuner.MaxProbability);
        // Recovered parameters should land near the generative truth.
        Assert.InRange(result.Slip, Slip - 0.08, Slip + 0.08);
        Assert.InRange(result.Guess, Guess - 0.08, Guess + 0.08);
        Assert.InRange(result.Learn, Learn - 0.15, Learn + 0.15);
    }

    [Fact]
    public void Tune_DegenerateAllCorrectSequences_ClampsToValidRange()
    {
        var sequences = Enumerable.Range(0, 5)
            .Select(_ => Enumerable.Repeat(true, 30).ToList())
            .ToList();

        var result = BktParameterTuner.Tune(sequences, new BktParameters());

        Assert.InRange(result.Slip, BktParameterTuner.MinProbability, BktParameterTuner.MaxProbability);
        Assert.InRange(result.Guess, BktParameterTuner.MinProbability, BktParameterTuner.MaxProbability);
        Assert.InRange(result.Learn, BktParameterTuner.MinProbability, BktParameterTuner.MaxProbability);
        Assert.True(double.IsFinite(result.LogLikelihood));
    }

    /// <summary>Deterministic latent-state sequences from the true BKT params.</summary>
    private static List<List<bool>> Synthesize(int sequenceCount, int length)
    {
        var rng = new Random(12345);
        var sequences = new List<List<bool>>();
        for (var i = 0; i < sequenceCount; i++)
        {
            var learned = rng.NextDouble() < Prior;
            var sequence = new List<bool>(length);
            for (var t = 0; t < length; t++)
            {
                sequence.Add(learned ? rng.NextDouble() >= Slip : rng.NextDouble() < Guess);
                if (!learned && rng.NextDouble() < Learn)
                {
                    learned = true;
                }
            }
            sequences.Add(sequence);
        }
        return sequences;
    }
}
