using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Services;
using VoxMentor.Domain.Entities;
using VoxMentor.Infrastructure.Persistence;

namespace VoxMentor.Infrastructure.Jobs;

/// <summary>
/// Nightly 02:00 UTC: EM-optimize BKT slip/guess/learn per concept from all
/// users' scored submissions (#57). Cross-user by design — a background job
/// has no HTTP user, so queries bypass the global user filter explicitly.
/// </summary>
[AutomaticRetry(Attempts = 1)]
[DisableConcurrentExecution(3600)]
public class BktParameterTuningJob
{
    public const int MinSubmissions = 50;

    private readonly ApplicationDbContext _db;
    private readonly ILogger<BktParameterTuningJob> _logger;

    public BktParameterTuningJob(ApplicationDbContext db, ILogger<BktParameterTuningJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var rows = await (
                from s in _db.CodeSubmissions.IgnoreQueryFilters()
                join q in _db.Questions on s.QuestionId equals q.Id
                where s.MasteryAppliedAt != null
                select new { q.ConceptId, s.UserId, s.IsCorrect, s.CreatedAt, s.Id })
            .AsNoTracking()
            .OrderBy(x => x.ConceptId)
            .ThenBy(x => x.UserId)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var tuned = 0;
        var kept = 0;
        var skipped = 0;

        foreach (var concept in rows.GroupBy(x => x.ConceptId))
        {
            if (concept.Count() < MinSubmissions)
            {
                skipped++;
                continue;
            }

            var sequences = concept
                .GroupBy(x => x.UserId)
                .Select(g => (IReadOnlyList<bool>)g.Select(x => x.IsCorrect).ToList())
                .ToList();

            var parameters = await _db.BktParameters
                .FirstOrDefaultAsync(p => p.ConceptId == concept.Key, cancellationToken);
            var isNew = parameters is null;
            parameters ??= new BktParameters { ConceptId = concept.Key };

            var oldSlip = parameters.SlipRate;
            var oldGuess = parameters.GuessRate;
            var oldLearn = parameters.LearnRate;
            var oldLl = BktParameterTuner.LogLikelihood(
                sequences, oldSlip, oldGuess, oldLearn, parameters.PriorKnowledge);
            var result = BktParameterTuner.Tune(sequences, parameters);

            if (result.LogLikelihood < oldLl)
            {
                kept++;
                _logger.LogDebug(
                    "Concept {ConceptId}: EM worsened likelihood ({OldLl:F2} -> {NewLl:F2}); keeping slip={Slip:F3} guess={Guess:F3} learn={Learn:F3}.",
                    concept.Key, oldLl, result.LogLikelihood, oldSlip, oldGuess, oldLearn);
                continue;
            }

            if (isNew)
            {
                _db.BktParameters.Add(parameters);
            }

            parameters.SlipRate = (float)result.Slip;
            parameters.GuessRate = (float)result.Guess;
            parameters.LearnRate = (float)result.Learn;
            parameters.UpdatedAt = DateTime.UtcNow;
            tuned++;

            _logger.LogDebug(
                "Concept {ConceptId} (n={Count}): slip {OldSlip:F3}->{Slip:F3}, guess {OldGuess:F3}->{Guess:F3}, learn {OldLearn:F3}->{Learn:F3}, ll {OldLl:F2}->{NewLl:F2} ({Iterations} iterations).",
                concept.Key, concept.Count(), oldSlip, result.Slip, oldGuess, result.Guess,
                oldLearn, result.Learn, oldLl, result.LogLikelihood, result.Iterations);
        }

        if (tuned > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "BKT parameter tuning: {Tuned} concepts tuned, {Kept} kept by likelihood guard, {Skipped} below {Min} submissions, {Rows} scored rows, {Elapsed} ms.",
            tuned, kept, skipped, MinSubmissions, rows.Count, stopwatch.ElapsedMilliseconds);
    }
}
