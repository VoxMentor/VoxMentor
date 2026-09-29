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
    [Fact]
    public async Task Execute_DecaysIdleRows_AcrossAllTenants_AndBumpsWatermark()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new TestCurrentUser { UserId = null }); // background job = no HTTP user → filter would hide everything

        var now = DateTime.UtcNow;
        var stale = new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = "user-a",
            ConceptId = Guid.NewGuid(),
            MasteryProbability = 0.8f,
            UpdatedAt = now.AddDays(-10) // idleDays = 10 - 7 = 3
        };
        var fresh = new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = "user-a",
            ConceptId = Guid.NewGuid(),
            MasteryProbability = 0.5f,
            UpdatedAt = now
        };
        var otherTenantStale = new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = "user-b",
            ConceptId = Guid.NewGuid(),
            MasteryProbability = 0.9f,
            UpdatedAt = now.AddDays(-10)
        };
        db.StudentMasteries.AddRange(stale, fresh, otherTenantStale);
        await db.SaveChangesAsync();

        var job = new SpacedRepetitionDecayJob(db, NullLogger<SpacedRepetitionDecayJob>.Instance);
        var after = DateTime.UtcNow;
        await job.ExecuteAsync(CancellationToken.None);

        var staleRow = await db.StudentMasteries.IgnoreQueryFilters().SingleAsync(m => m.Id == stale.Id);
        var freshRow = await db.StudentMasteries.IgnoreQueryFilters().SingleAsync(m => m.Id == fresh.Id);
        var otherRow = await db.StudentMasteries.IgnoreQueryFilters().SingleAsync(m => m.Id == otherTenantStale.Id);

        Assert.Equal(0.8f * MathF.Pow(0.95f, 3), staleRow.MasteryProbability, 5);
        Assert.True(staleRow.UpdatedAt >= after, "decay must bump the UpdatedAt watermark");
        Assert.Equal(0.5f, freshRow.MasteryProbability, 5);
        Assert.Equal(fresh.UpdatedAt, freshRow.UpdatedAt); // fresh row untouched, watermark too
        Assert.Equal(0.9f * MathF.Pow(0.95f, 3), otherRow.MasteryProbability, 5);
        Assert.True(otherRow.UpdatedAt >= after, "other tenants must be decayed too");
    }
}
