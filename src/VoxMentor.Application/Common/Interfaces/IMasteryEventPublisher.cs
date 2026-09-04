using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Publishes mastery-updated events after a successful answer submission.
/// Backed by Redis Streams once issue #4 lands; the null implementation logs only.
/// </summary>
public interface IMasteryEventPublisher
{
    /// <summary>
    /// Notifies subscribers that a student's mastery changed.
    /// </summary>
    /// <param name="mastery">The persisted mastery row after the update.</param>
    /// <param name="previousMastery">Mastery probability before this answer was applied.</param>
    Task PublishMasteryUpdatedAsync(StudentMastery mastery, float previousMastery, CancellationToken cancellationToken = default);
}
