using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Services;
using VoxMentor.Infrastructure.Persistence;

namespace VoxMentor.Infrastructure.Jobs;

/// <summary>
/// Nightly 03:00 UTC: spaced-repetition decay — mastery rows idle past the
/// grace window lose 5% per idle day down to the 0.1 floor (#57). Cross-user
/// by design: enumerates every tenant's rows, bypassing the global user
/// filter explicitly (a background job has no HTTP user).
/// </summary>
[AutomaticRetry(Attempts = 1)]
[DisableConcurrentExecution(3600)]
public class SpacedRepetitionDecayJob
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SpacedRepetitionDecayJob> _logger;

    public SpacedRepetitionDecayJob(ApplicationDbContext db, ILogger<SpacedRepetitionDecayJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var rows = await _db.StudentMasteries
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);

        var changed = 0;
        foreach (var mastery in rows)
        {
            var next = MasteryDecay.Compute(mastery.MasteryProbability, mastery.UpdatedAt, now);
            if (next is null)
            {
                continue;
            }

            var value = (float)next.Value;
            if (value == mastery.MasteryProbability)
            {
                continue; // already at the floor — nothing to write
            }

            mastery.MasteryProbability = value;
            mastery.UpdatedAt = now; // watermark: next decay waits a fresh grace window
            changed++;
        }

        if (changed > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Spaced-repetition decay: {Changed} of {Rows} mastery rows decayed.", changed, rows.Count);
    }
}
