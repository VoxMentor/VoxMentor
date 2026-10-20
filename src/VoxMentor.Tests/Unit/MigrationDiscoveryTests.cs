using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector.EntityFrameworkCore;
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

    [Fact]
    public void ModelSnapshot_MatchesRuntimeModel()
    {
        // The EF-recommended pending-model-changes check: scaffolding the next
        // migration against a drifted snapshot would emit spurious operations.
        // The relational differ ignores core-only metadata such as concurrency
        // tokens, so those are compared separately below — RunId had lost its
        // token in the hand-updated snapshot (#104 round 6).
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=snapshot-parity;Username=parity;Password=parity",
                o => o.UseVector())
            .Options;
        using var context = new ApplicationDbContext(options, new TestCurrentUser());

        var differ = context.GetService<IMigrationsModelDiffer>();
        var initializer = context.GetService<IModelRuntimeInitializer>();
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        if (snapshot is IMutableModel mutableSnapshot)
        {
            snapshot = mutableSnapshot.FinalizeModel();
        }

        snapshot = initializer.Initialize(snapshot);
        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.Empty(differ.GetDifferences(
            snapshot.GetRelationalModel(),
            model.GetRelationalModel()));

        foreach (var entity in model.GetEntityTypes())
        {
            var snapshotEntity = snapshot.FindEntityType(entity.Name);
            if (snapshotEntity is null)
            {
                continue; // relational diff already reports a missing entity
            }

            foreach (var property in entity.GetProperties().Where(p => p.IsConcurrencyToken))
            {
                Assert.True(
                    snapshotEntity.FindProperty(property.Name)?.IsConcurrencyToken == true,
                    $"{entity.Name}.{property.Name} lost its concurrency token in the snapshot");
            }
        }
    }
}
