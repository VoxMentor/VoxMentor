using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Api.Hubs;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Api.Services;

/// <summary>
/// Publishes MasteryUpdated events to /hubs/mastery for the student who owns
/// the mastery row. Replaces the NullMasteryEventPublisher registration so the
/// BKT handlers (SubmitAnswer/SubmitCode) stay untouched.
/// </summary>
public class SignalRMasteryEventPublisher : IMasteryEventPublisher
{
    private readonly IHubContext<MasteryHub> _hub;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<SignalRMasteryEventPublisher> _logger;

    public SignalRMasteryEventPublisher(
        IHubContext<MasteryHub> hub,
        IApplicationDbContext db,
        ILogger<SignalRMasteryEventPublisher> logger)
    {
        _hub = hub;
        _db = db;
        _logger = logger;
    }

    public async Task PublishMasteryUpdatedAsync(
        StudentMastery mastery,
        float previousMastery,
        CancellationToken cancellationToken = default)
    {
        // ponytail: fire-and-forget — do not let SignalR failures break the answer-submission flow.
        // The mastery row is already persisted; the push is best-effort.
        try
        {
            var conceptName = await _db.Concepts
                .Where(c => c.Id == mastery.ConceptId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);

            var delta = mastery.MasteryProbability - previousMastery;
            await _hub.Clients.User(mastery.UserId).SendAsync(
                "MasteryUpdated",
                new
                {
                    conceptId = mastery.ConceptId,
                    conceptName = conceptName ?? string.Empty,
                    newMastery = mastery.MasteryProbability,
                    delta
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish MasteryUpdated for User {UserId} Concept {ConceptId}",
                mastery.UserId, mastery.ConceptId);
        }

        _logger.LogInformation(
            "MasteryUpdated: User {UserId} Concept {ConceptId} {Previous} -> {Current}",
            mastery.UserId, mastery.ConceptId, previousMastery, mastery.MasteryProbability);
    }
}
