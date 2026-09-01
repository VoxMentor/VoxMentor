using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Services;

public class BktEngine : IBktEngine
{
    public float UpdateMastery(float currentMastery, BktParameters p, bool correct)
    {
        float pL = currentMastery;

        // P(correct) = P(C|L)*P(L) + P(C|~L)*P(~L)
        float pCorrect = (1 - p.SlipRate) * pL + p.GuessRate * (1 - pL);

        float newMastery;
        if (correct)
        {
            // P(L | correct) = P(C|L)*P(L) / P(C)
            newMastery = ((1 - p.SlipRate) * pL) / pCorrect;
        }
        else
        {
            // P(L | incorrect) = P(C|~L)*P(L) / P(~C)
            float pIncorrect = p.SlipRate * pL + (1 - p.GuessRate) * (1 - pL);
            newMastery = (p.SlipRate * pL) / pIncorrect;
        }

        // Apply learning transition (only on correct answers)
        if (correct)
            newMastery = newMastery + (1 - newMastery) * p.LearnRate;

        return Math.Clamp(newMastery, 0f, 1f);
    }

    public float UpdateMastery(float currentMastery, BktParameters p, IEnumerable<bool> observations)
    {
        float mastery = currentMastery;
        foreach (var correct in observations)
            mastery = UpdateMastery(mastery, p, correct);
        return mastery;
    }
}
