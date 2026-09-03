using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Common.Interfaces;

public interface IMasteryEventPublisher
{
    Task PublishMasteryUpdatedAsync(StudentMastery mastery, float previousMastery, CancellationToken cancellationToken = default);
}
