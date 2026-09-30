using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Services;

/// <summary>
/// EM (Baum-Welch style) tuning of BKT slip/guess/learn per concept (#57).
/// Pure math — no I/O. Callers pass per-user observation sequences ordered by
/// (CreatedAt, Id) and apply the result behind a likelihood guard.
/// Prior is not optimized (issue: tune slip/guess/learn only).
/// </summary>
public static class BktParameterTuner
{
    public const int MaxIterations = 20;
    public const double Tolerance = 1e-3;
    public const double MinProbability = 0.01;
    public const double MaxProbability = 0.99;

    public readonly record struct TunedParameters(
        double Slip,
        double Guess,
        double Learn,
        double LogLikelihood,
        int Iterations);

    /// <summary>Log-likelihood of the observation sequences under the given parameters.</summary>
    public static double LogLikelihood(
        IReadOnlyList<IReadOnlyList<bool>> sequences,
        double slip,
        double guess,
        double learn,
        double prior)
    {
        double ll = 0;
        foreach (var sequence in sequences)
        {
            double p = prior;
            foreach (var correct in sequence)
            {
                double pCorrect = (1 - slip) * p + guess * (1 - p);
                double pObserved = correct ? pCorrect : 1 - pCorrect;
                ll += Math.Log(Math.Max(pObserved, 1e-12));
                p = Posterior(p, correct, slip, guess);
                p += (1 - p) * learn;
            }
        }
        return ll;
    }

    /// <summary>Runs EM until the log-likelihood converges or MaxIterations is hit.</summary>
    public static TunedParameters Tune(
        IReadOnlyList<IReadOnlyList<bool>> sequences,
        BktParameters current)
    {
        double prior = current.PriorKnowledge;
        double slip = current.SlipRate;
        double guess = current.GuessRate;
        double learn = current.LearnRate;
        double ll = LogLikelihood(sequences, slip, guess, learn, prior);

        int iteration;
        for (iteration = 1; iteration <= MaxIterations; iteration++)
        {
            // E-step: forward-backward per sequence accumulates emission and
            // transition expected counts. ponytail: unscaled probabilities —
            // per-user/concept sequences are short enough not to underflow.
            double missGivenLearned = 0, learned = 0;
            double hitGivenUnknown = 0, unknown = 0;
            double unknownToLearned = 0, unknownBeforeTransition = 0;

            foreach (var sequence in sequences)
            {
                if (sequence.Count == 0) continue;
                int n = sequence.Count;
                var pred = new double[n];   // P(L before obs t | obs < t)
                var rL = new double[n + 1]; // P(o_t..o_{n-1} | L before obs t)
                var rN = new double[n + 1]; // same, given not-L

                double p = prior;
                for (int t = 0; t < n; t++)
                {
                    pred[t] = p;
                    p = Posterior(p, sequence[t], slip, guess);
                    p += (1 - p) * learn;
                }

                rL[n] = 1;
                rN[n] = 1;
                for (int t = n - 1; t >= 0; t--)
                {
                    bool correct = sequence[t];
                    double givenL = correct ? 1 - slip : slip;
                    double givenNotL = correct ? guess : 1 - guess;
                    rL[t] = givenL * rL[t + 1];
                    rN[t] = givenNotL * (learn * rL[t + 1] + (1 - learn) * rN[t + 1]);
                }

                for (int t = 0; t < n; t++)
                {
                    double z = pred[t] * rL[t] + (1 - pred[t]) * rN[t];
                    if (z <= 0) continue;
                    // Smoothed P(L before obs t | all observations).
                    double gamma = pred[t] * rL[t] / z;

                    // Emission denominators span every observation; numerators
                    // are split by outcome (classic Baum-Welch counts).
                    learned += gamma;
                    unknown += 1 - gamma;
                    if (sequence[t])
                    {
                        hitGivenUnknown += 1 - gamma;
                    }
                    else
                    {
                        missGivenLearned += gamma;
                    }

                    if (t < n - 1)
                    {
                        // P(not-L before obs t, L before obs t+1 | all obs).
                        double givenNotL = sequence[t] ? guess : 1 - guess;
                        unknownToLearned += (1 - pred[t]) * givenNotL * learn * rL[t + 1] / z;
                        unknownBeforeTransition += 1 - gamma;
                    }
                }
            }

            // M-step, clamped away from the degenerate 0/1 boundaries.
            slip = Math.Clamp(learned > 0 ? missGivenLearned / learned : slip, MinProbability, MaxProbability);
            guess = Math.Clamp(unknown > 0 ? hitGivenUnknown / unknown : guess, MinProbability, MaxProbability);
            learn = Math.Clamp(
                unknownBeforeTransition > 1e-9 ? unknownToLearned / unknownBeforeTransition : learn,
                MinProbability, MaxProbability);

            double newLl = LogLikelihood(sequences, slip, guess, learn, prior);
            bool converged = Math.Abs(newLl - ll) < Tolerance;
            ll = newLl;
            if (converged) break;
        }

        return new TunedParameters(slip, guess, learn, ll, Math.Min(iteration, MaxIterations));
    }

    private static double Posterior(double p, bool correct, double slip, double guess)
    {
        double pCorrect = (1 - slip) * p + guess * (1 - p);
        if (correct)
            return pCorrect > 0 ? ((1 - slip) * p) / pCorrect : p;
        double pIncorrect = slip * p + (1 - guess) * (1 - p);
        return pIncorrect > 0 ? (slip * p) / pIncorrect : p;
    }
}
