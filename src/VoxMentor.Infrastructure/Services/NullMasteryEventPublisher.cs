using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// No-op publisher until Redis Streams lands (issue #4).
/// Keeps the CQRS pipeline testable and deployable without Redis infra.
/// </summary>
public class NullMasteryEventPublisher : IMasteryEventPublisher
{
    private readonly ILogger<NullMasteryEventPublisher> _logger;

    public NullMasteryEventPublisher(ILogger<NullMasteryEventPublisher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Logs the mastery transition without emitting an external event.
    /// </summary>
    public Task PublishMasteryUpdatedAsync(StudentMastery mastery, float previousMastery, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "MasteryUpdated (not emitted): User {UserId} Concept {ConceptId} {Previous} -> {Current}",
            mastery.UserId, mastery.ConceptId, previousMastery, mastery.MasteryProbability);
        return Task.CompletedTask;
    }
}
