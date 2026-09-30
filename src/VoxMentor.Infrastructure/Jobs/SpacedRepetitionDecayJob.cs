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
    // ponytail: fixed batch size; row count is bounded by students×concepts, raise if the table grows huge
    private const int BatchSize = 500;

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
        var cutoff = now.AddDays(-MasteryDecay.GraceDays); // pre-filter only; Compute is the authority
        var changed = 0;
        var conflicts = 0;
        var scanned = 0;
        var skip = 0;

        while (true)
        {
            // Idle anchor (LastPracticedAt ?? CreatedAt) never moves during decay,
            // so the filtered set is stable and Skip/Take paging stays consistent.
            var batch = await _db.StudentMasteries
                .IgnoreQueryFilters()
                .Where(m => (m.LastPracticedAt ?? m.CreatedAt) <= cutoff)
                .OrderBy(m => m.Id)
                .Skip(skip)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            skip += batch.Count;
            scanned += batch.Count;
            var batchChanged = 0;

            foreach (var mastery in batch)
            {
                var anchor = mastery.LastPracticedAt ?? mastery.CreatedAt;
                var next = MasteryDecay.Compute(mastery.MasteryProbability, anchor, mastery.UpdatedAt, now);
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
                mastery.UpdatedAt = now; // last-write watermark: counts applied idle days, anchor untouched
                batchChanged++;
            }

            if (batchChanged > 0)
            {
                changed += batchChanged;
                conflicts += await SaveSkippingConflictsAsync(cancellationToken);
            }

            _db.ChangeTracker.Clear(); // bound memory: rows are re-read from SQL next page, not kept tracked
        }

        _logger.LogInformation(
            "Spaced-repetition decay: {Changed} of {Scanned} mastery rows decayed ({Conflicts} skipped on concurrent writes).",
            changed, scanned, conflicts);
    }

    /// <summary>
    /// Saves the current batch; a row touched by a concurrent submission (xmin
    /// mismatch) is detached and dropped instead of failing every other row.
    /// </summary>
    private async Task<int> SaveSkippingConflictsAsync(CancellationToken cancellationToken)
    {
        var conflicts = 0;
        while (true)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return conflicts;
            }
            catch (DbUpdateConcurrencyException ex) when (ex.Entries.Count > 0)
            {
                foreach (var entry in ex.Entries)
                {
                    entry.State = EntityState.Detached;
                }

                conflicts += ex.Entries.Count; // skipped this run, retried by tomorrow's run
            }
        }
    }
}
