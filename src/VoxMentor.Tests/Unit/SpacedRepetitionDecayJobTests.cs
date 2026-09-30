using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VoxMentor.Domain.Entities;
using VoxMentor.Infrastructure.Jobs;
using VoxMentor.Infrastructure.Persistence;
using Xunit;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// The decay job runs with no HTTP user (filter matches nothing) and must
/// still enumerate every tenant's rows — IgnoreQueryFilters is load-bearing,
/// this test fails if it is removed.
/// </summary>
public class SpacedRepetitionDecayJobTests
{
    private static ApplicationDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new TestCurrentUser { UserId = null }); // background job = no HTTP user → filter would hide everything

    [Fact]
    public async Task Execute_DecaysIdleRows_AcrossAllTenants_AndBumpsWatermark()
    {
        await using var db = NewDb();

        var now = DateTime.UtcNow;
        var stale = new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = "user-a",
            ConceptId = Guid.NewGuid(),
            MasteryProbability = 0.8f,
            LastPracticedAt = now.AddDays(-10), // anchor; idleDays = 10 - 7 = 3
            UpdatedAt = now.AddDays(-10) // never decayed yet
        };
        var fresh = new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = "user-a",
            ConceptId = Guid.NewGuid(),
            MasteryProbability = 0.5f,
            LastPracticedAt = now,
            UpdatedAt = now
        };
        var otherTenantStale = new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = "user-b",
            ConceptId = Guid.NewGuid(),
            MasteryProbability = 0.9f,
            LastPracticedAt = now.AddDays(-10),
            UpdatedAt = now.AddDays(-10)
        };
        var createdOnly = new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = "user-a",
            ConceptId = Guid.NewGuid(),
            MasteryProbability = 0.6f,
            LastPracticedAt = null, // never practiced → CreatedAt is the anchor
            CreatedAt = now.AddDays(-10),
            UpdatedAt = now.AddDays(-10)
        };
        db.StudentMasteries.AddRange(stale, fresh, otherTenantStale, createdOnly);
        await db.SaveChangesAsync();

        var job = new SpacedRepetitionDecayJob(db, NullLogger<SpacedRepetitionDecayJob>.Instance);
        var after = DateTime.UtcNow;
        await job.ExecuteAsync(CancellationToken.None);

        var staleRow = await db.StudentMasteries.IgnoreQueryFilters().SingleAsync(m => m.Id == stale.Id);
        var freshRow = await db.StudentMasteries.IgnoreQueryFilters().SingleAsync(m => m.Id == fresh.Id);
        var otherRow = await db.StudentMasteries.IgnoreQueryFilters().SingleAsync(m => m.Id == otherTenantStale.Id);
        var createdRow = await db.StudentMasteries.IgnoreQueryFilters().SingleAsync(m => m.Id == createdOnly.Id);

        Assert.Equal(0.8f * MathF.Pow(0.95f, 3), staleRow.MasteryProbability, 4);
        Assert.True(staleRow.UpdatedAt >= after, "decay must bump the UpdatedAt watermark");
        Assert.Equal(0.5f, freshRow.MasteryProbability, 5);
        Assert.Equal(fresh.UpdatedAt, freshRow.UpdatedAt); // fresh row untouched, watermark too
        Assert.Equal(0.9f * MathF.Pow(0.95f, 3), otherRow.MasteryProbability, 4);
        Assert.True(otherRow.UpdatedAt >= after, "other tenants must be decayed too");
        Assert.Equal(0.6f * MathF.Pow(0.95f, 3), createdRow.MasteryProbability, 4);
    }

    [Fact]
    public async Task Execute_SecondRunSameDay_DoesNotDoubleDecay()
    {
        await using var db = NewDb();

        var now = DateTime.UtcNow;
        var stale = new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = "user-a",
            ConceptId = Guid.NewGuid(),
            MasteryProbability = 0.8f,
            LastPracticedAt = now.AddDays(-10),
            UpdatedAt = now.AddDays(-10)
        };
        db.StudentMasteries.Add(stale);
        await db.SaveChangesAsync();

        var job = new SpacedRepetitionDecayJob(db, NullLogger<SpacedRepetitionDecayJob>.Instance);
        await job.ExecuteAsync(CancellationToken.None);
        var afterFirst = (await db.StudentMasteries.IgnoreQueryFilters().SingleAsync(m => m.Id == stale.Id)).MasteryProbability;

        await job.ExecuteAsync(CancellationToken.None);
        var afterSecond = (await db.StudentMasteries.IgnoreQueryFilters().SingleAsync(m => m.Id == stale.Id)).MasteryProbability;

        Assert.Equal(0.8f * MathF.Pow(0.95f, 3), afterFirst, 4);
        Assert.Equal(afterFirst, afterSecond); // watermark counts applied idle days → idempotent same-day re-run
    }

    [Fact]
    public async Task Execute_MoreRowsThanOneBatch_DecaysEveryRow()
    {
        await using var db = NewDb();

        var now = DateTime.UtcNow;
        const int total = 550; // > BatchSize (500), so at least two pages
        for (var i = 0; i < total; i++)
        {
            db.StudentMasteries.Add(new StudentMastery
            {
                Id = Guid.NewGuid(),
                UserId = "user-a",
                ConceptId = Guid.NewGuid(),
                MasteryProbability = 0.7f,
                LastPracticedAt = now.AddDays(-10),
                UpdatedAt = now.AddDays(-10)
            });
        }

        await db.SaveChangesAsync();

        var job = new SpacedRepetitionDecayJob(db, NullLogger<SpacedRepetitionDecayJob>.Instance);
        await job.ExecuteAsync(CancellationToken.None);

        var expected = 0.7f * MathF.Pow(0.95f, 3);
        var rows = await db.StudentMasteries.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(total, rows.Count);
        Assert.All(rows, r => Assert.Equal(expected, r.MasteryProbability, 4));
    }
}
