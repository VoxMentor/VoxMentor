using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoxMentor.Infrastructure.Persistence;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Hand-written migrations must carry discovery metadata. EF Core's
/// MigrateAsync registers only Migration-derived types carrying BOTH
/// [DbContext] and [Migration]; the pair normally lives in the generated
/// Designer.cs, which hand-written migrations do not have — so losing it is
/// a plain editing mistake that would silently skip the migration
/// (#104 CodeRabbit round 5).
/// </summary>
public class MigrationDiscoveryTests
{
    [Fact]
    public void EveryMigration_IsDiscoverableByMigrateAsync()
    {
        var migrations = typeof(ApplicationDbContext).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false } && typeof(Migration).IsAssignableFrom(t))
            .Select(t => new
            {
                Type = t,
                DbContext = t.GetCustomAttribute<DbContextAttribute>(),
                Migration = t.GetCustomAttribute<MigrationAttribute>()
            })
            .ToList();

        Assert.NotEmpty(migrations);
        foreach (var migration in migrations)
        {
            Assert.Equal(typeof(ApplicationDbContext), migration.DbContext?.ContextType);
            Assert.False(
                string.IsNullOrWhiteSpace(migration.Migration?.Id),
                $"{migration.Type.Name} is missing its [Migration] id");
        }

        var ids = migrations.Select(m => m.Migration!.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
