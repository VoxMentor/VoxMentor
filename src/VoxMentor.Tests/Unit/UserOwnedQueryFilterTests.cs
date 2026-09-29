using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Interfaces;
using VoxMentor.Infrastructure.Persistence;
using Xunit;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Global IUserOwned query-filter coverage (#57): every mapped marked entity is
/// scoped to the current user, an unauthenticated context sees nothing
/// (fail closed), and IgnoreQueryFilters() is the only way to cross users.
/// </summary>
public class UserOwnedQueryFilterTests
{
    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public string? UserId { get; set; }
    }

    private static ApplicationDbContext CreateDb(FakeCurrentUser user)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, user);
    }

    /// <summary>All mapped IUserOwned CLR types — picks up entities added later.</summary>
    private static IReadOnlyList<Type> OwnedTypes(ApplicationDbContext db)
        => db.Model.GetEntityTypes()
            .Where(e => typeof(IUserOwned).IsAssignableFrom(e.ClrType))
            .Select(e => e.ClrType)
            .OrderBy(t => t.Name)
            .ToList();

    private static IUserOwned NewOwnedInstance(Type type, string userId)
    {
        var entity = (IUserOwned)Activator.CreateInstance(type)!;
        entity.UserId = userId;
        // Current key convention: Guid Id — Activator leaves Guid.Empty otherwise.
        var id = type.GetProperty("Id");
        if (id?.PropertyType == typeof(Guid))
        {
            id.SetValue(entity, Guid.NewGuid());
        }
        // Unique-index column: distinct per row or InMemory rejects the insert.
        if (type == typeof(RefreshToken))
        {
            ((RefreshToken)entity).TokenHash = Guid.NewGuid().ToString();
        }
        return entity;
    }

    private static void SeedBothUsers(ApplicationDbContext db)
    {
        foreach (var type in OwnedTypes(db))
        {
            db.Add(NewOwnedInstance(type, "user-a"));
            db.Add(NewOwnedInstance(type, "user-b"));
        }
        db.SaveChanges();
    }

    /// <summary>Non-generic DbSet access — DbContext.Set() is generic-only.</summary>
    private static IQueryable SetQuery(ApplicationDbContext db, Type type)
        => (IQueryable)typeof(DbContext)
            .GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
            .MakeGenericMethod(type)
            .Invoke(db, null)!;

    private static List<string> VisibleOwnerIds(ApplicationDbContext db, Type type)
        => SetQuery(db, type)
            .Cast<object>()
            .ToList()
            .Select(e => (string)type.GetProperty("UserId")!.GetValue(e)!)
            .Distinct()
            .ToList();

    [Fact]
    public void Queries_ReturnOnlyCurrentUsersRows_ForEveryOwnedEntity()
    {
        using var db = CreateDb(new FakeCurrentUser { UserId = "user-a" });
        SeedBothUsers(db);

        foreach (var type in OwnedTypes(db))
        {
            Assert.Equal(["user-a"], VisibleOwnerIds(db, type));
        }
    }

    [Fact]
    public void Queries_UnauthenticatedContext_ReturnsNoRows_ForEveryOwnedEntity()
    {
        using var db = CreateDb(new FakeCurrentUser { UserId = null });
        SeedBothUsers(db);

        foreach (var type in OwnedTypes(db))
        {
            Assert.Empty(SetQuery(db, type).Cast<object>().ToList());
        }
    }

    [Fact]
    public void IgnoreQueryFilters_SeesBothUsersRows()
    {
        using var db = CreateDb(new FakeCurrentUser { UserId = "user-a" });
        db.Add(NewOwnedInstance(typeof(StudentMastery), "user-a"));
        db.Add(NewOwnedInstance(typeof(StudentMastery), "user-b"));
        db.SaveChanges();

        var rows = db.StudentMasteries.IgnoreQueryFilters().ToList();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.UserId == "user-a");
        Assert.Contains(rows, r => r.UserId == "user-b");
    }
}
