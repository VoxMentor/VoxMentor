using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoxMentor.Infrastructure.Migrations;
using VoxMentor.Infrastructure.Persistence;
using Xunit;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Hand-written migrations must carry discovery metadata. EF Core's
/// MigrateAsync registers only Migration-derived types carrying BOTH
/// [DbContext] and [Migration] — an unregistered migration silently
/// never creates its columns (#104 CodeRabbit round 5).
/// </summary>
public class MigrationDiscoveryTests
{
    [Fact]
    public void AddTextbookJobRunId_IsDiscoverableByMigrateAsync()
    {
        var db = typeof(AddTextbookJobRunId).GetCustomAttribute<DbContextAttribute>();
        var mig = typeof(AddTextbookJobRunId).GetCustomAttribute<MigrationAttribute>();

        Assert.Equal(typeof(ApplicationDbContext), db?.ContextType);
        Assert.Equal("20261020120000_AddTextbookJobRunId", mig?.Id);
    }

    [Fact]
    public void AddTextbookJobClaimedAt_IsDiscoverableByMigrateAsync()
    {
        var db = typeof(AddTextbookJobClaimedAt).GetCustomAttribute<DbContextAttribute>();
        var mig = typeof(AddTextbookJobClaimedAt).GetCustomAttribute<MigrationAttribute>();

        Assert.Equal(typeof(ApplicationDbContext), db?.ContextType);
        Assert.Equal("20261020124500_AddTextbookJobClaimedAt", mig?.Id);
    }
}
