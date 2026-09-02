using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Services;

public interface IBktEngine
{
    float UpdateMastery(float currentMastery, BktParameters parameters, bool correct);
    float UpdateMastery(float currentMastery, BktParameters parameters, IEnumerable<bool> observations);
}
