using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;
using VoxMentor.Infrastructure.Persistence;
using VoxMentor.Infrastructure.Persistence.Seeders;
using Xunit;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="DemoSeeder"/> covering the production gate,
/// missing-concepts fail-fast, and defensive config parsing.
/// </summary>
public class DemoSeederTests
{
    private sealed class NullCurrentUser : ICurrentUserService
    {
        public string? UserId => null;
    }

    private static ApplicationDbContext CreateDb()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new NullCurrentUser());

    /// <summary>
    /// Builds an <see cref="IServiceProvider"/> with DbContext, UserManager, and RoleManager
    /// registered as singletons around the given InMemory database instance.
    /// </summary>
    private static IServiceProvider BuildServices(ApplicationDbContext db)
    {
        var userManager = CreateUserManager(db);
        var roleManager = CreateRoleManager(db);

        var services = new ServiceCollection();
        services.AddSingleton<ApplicationDbContext>(db);
        services.AddSingleton<UserManager<ApplicationUser>>(userManager);
        services.AddSingleton<RoleManager<IdentityRole>>(roleManager);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a <see cref="RoleManager{IdentityRole}"/> backed by the given InMemory database.
    /// </summary>
    private static RoleManager<IdentityRole> CreateRoleManager(ApplicationDbContext db)
        => new(
            new RoleStore<IdentityRole>(db),
            Enumerable.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);

    /// <summary>
    /// Seeds the Student and ContentAdmin roles if they do not already exist.
    /// </summary>
    private static async Task SeedRolesAsync(IServiceProvider services, ApplicationDbContext db)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Student", "ContentAdmin" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Builds an <see cref="IConfiguration"/> with optional in-memory key-value pairs.
    /// </summary>
    private static IConfiguration Config(Action<Microsoft.Extensions.Configuration.ConfigurationBuilder>? configure = null)
    {
        var builder = new ConfigurationBuilder();
        configure?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>
    /// Seeds the 10 DSA concepts required by the demo seed into the database.
    /// </summary>
    private static async Task SeedConceptsAsync(ApplicationDbContext db)
    {
        foreach (var id in DemoSeederTestsExposed.ExistingConceptIds())
        {
            db.Concepts.Add(new Concept
            {
                Id = id,
                Name = $"Concept {id}",
                Description = "seed concept",
                DifficultyLevel = 1,
                Category = "Fundamentals"
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SeedDemoDataAsync_Disabled_When_SeedDemoFalse()
    {
        await using var db = CreateDb();
        var config = Config(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:Demo"] = "false",
        }));

        await DemoSeeder.SeedDemoDataAsync(BuildServices(db), config);

        Assert.Empty(db.Users.IgnoreQueryFilters());
        Assert.Empty(db.JobDescriptions.IgnoreQueryFilters());
        Assert.Empty(db.MockInterviews.IgnoreQueryFilters());
        Assert.Empty(db.TutorSessions.IgnoreQueryFilters());
        Assert.Empty(db.StudentMasteries.IgnoreQueryFilters());
    }

    [Fact]
    public async Task SeedDemoDataAsync_Disabled_When_SeedDemoUnset()
    {
        await using var db = CreateDb();
        var config = Config();

        await DemoSeeder.SeedDemoDataAsync(BuildServices(db), config);

        Assert.Empty(db.Users.IgnoreQueryFilters());
    }

    [Fact]
    public async Task SeedDemoDataAsync_Throws_When_ConceptsMissing()
    {
        await using var db = CreateDb();
        var config = Config(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:Demo"] = "true",
            ["Seed:DemoPassword"] = "Test123!",
        }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DemoSeeder.SeedDemoDataAsync(BuildServices(db), config));

        Assert.Contains("concepts", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SeedDemoDataAsync_Throws_When_DemoPasswordMissing()
    {
        await using var db = CreateDb();
        await SeedConceptsAsync(db);
        var config = Config(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:Demo"] = "true",
        }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DemoSeeder.SeedDemoDataAsync(BuildServices(db), config));

        Assert.Contains("Seed:DemoPassword", ex.Message);
    }

    [Fact]
    public async Task UserManagerCreateAsync_SavesToInMemory()
    {
        await using var db = CreateDb();
        var userManager = CreateUserManager(db);
        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "u@example.com",
            Email = "u@example.com",
            FullName = "U"
        };
        var result = await userManager.CreateAsync(user, "Test123!");
        Assert.True(result.Succeeded);
        Assert.Equal(1, db.Users.Count());
    }

    [Fact]
    public static void IsDemoSeedEnabled_Parses_Defensively()
    {
        Assert.True(DemoSeeder.IsDemoSeedEnabled("true"));
        Assert.True(DemoSeeder.IsDemoSeedEnabled("TRUE"));
        Assert.True(DemoSeeder.IsDemoSeedEnabled("1"));
        Assert.True(DemoSeeder.IsDemoSeedEnabled("yes"));
        Assert.True(DemoSeeder.IsDemoSeedEnabled("on"));
        Assert.False(DemoSeeder.IsDemoSeedEnabled("false"));
        Assert.False(DemoSeeder.IsDemoSeedEnabled("0"));
        Assert.False(DemoSeeder.IsDemoSeedEnabled("no"));
        Assert.False(DemoSeeder.IsDemoSeedEnabled("off"));
        Assert.False(DemoSeeder.IsDemoSeedEnabled("garbage"));
        Assert.False(DemoSeeder.IsDemoSeedEnabled(null));
        Assert.False(DemoSeeder.IsDemoSeedEnabled(""));
    }

    [Fact]
    public async Task SeedDemoDataAsync_Idempotent_OnRerun_SameEntityCounts()
    {
        await using var db = CreateDb();
        await SeedConceptsAsync(db);
        await SeedRolesAsync(BuildServices(db), db);
        var config = Config(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:Demo"] = "true",
            ["Seed:DemoPassword"] = "Test123!",
        }));

        var services = BuildServices(db);

        // First run
        await DemoSeeder.SeedDemoDataAsync(services, config);
        var firstRunCounts = new
        {
            Users = db.Users.IgnoreQueryFilters().Count(),
            JobDescriptions = db.JobDescriptions.IgnoreQueryFilters().Count(),
            JdSkillWeights = db.JdSkillWeights.IgnoreQueryFilters().Count(),
            MockInterviews = db.MockInterviews.IgnoreQueryFilters().Count(),
            TutorSessions = db.TutorSessions.IgnoreQueryFilters().Count(),
            StudentMasteries = db.StudentMasteries.IgnoreQueryFilters().Count(),
        };

        // Assert expected first-run totals
        Assert.Equal(2, firstRunCounts.Users);
        Assert.Equal(2, firstRunCounts.JobDescriptions);
        Assert.Equal(10, firstRunCounts.JdSkillWeights);
        Assert.Equal(2, firstRunCounts.MockInterviews);
        Assert.Equal(3, firstRunCounts.TutorSessions);
        Assert.Equal(10, firstRunCounts.StudentMasteries);

        // Second run (should be idempotent)
        await DemoSeeder.SeedDemoDataAsync(services, config);
        var secondRunCounts = new
        {
            Users = db.Users.IgnoreQueryFilters().Count(),
            JobDescriptions = db.JobDescriptions.IgnoreQueryFilters().Count(),
            JdSkillWeights = db.JdSkillWeights.IgnoreQueryFilters().Count(),
            MockInterviews = db.MockInterviews.IgnoreQueryFilters().Count(),
            TutorSessions = db.TutorSessions.IgnoreQueryFilters().Count(),
            StudentMasteries = db.StudentMasteries.IgnoreQueryFilters().Count(),
        };

        Assert.Equal(firstRunCounts.Users, secondRunCounts.Users);
        Assert.Equal(firstRunCounts.JobDescriptions, secondRunCounts.JobDescriptions);
        Assert.Equal(firstRunCounts.JdSkillWeights, secondRunCounts.JdSkillWeights);
        Assert.Equal(firstRunCounts.MockInterviews, secondRunCounts.MockInterviews);
        Assert.Equal(firstRunCounts.TutorSessions, secondRunCounts.TutorSessions);
        Assert.Equal(firstRunCounts.StudentMasteries, secondRunCounts.StudentMasteries);
    }

    /// <summary>
    /// Creates a <see cref="UserManager{ApplicationUser}"/> backed by the given InMemory database.
    /// </summary>
    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
        => new(
            new UserStore<ApplicationUser>(db),
            new Microsoft.Extensions.Options.OptionsWrapper<IdentityOptions>(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Enumerable.Empty<IUserValidator<ApplicationUser>>(),
            Enumerable.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);
}

/// <summary>
/// Test-only access to DemoSeeder's internal <see cref="ExistingConceptIds"/> for test seeding.
/// </summary>
internal static class DemoSeederTestsExposed
{
    public static Guid[] ExistingConceptIds() =>
    [
        Guid.Parse("a0000001-0000-0000-0000-000000000001"),
        Guid.Parse("a0000001-0000-0000-0000-000000000002"),
        Guid.Parse("a0000001-0000-0000-0000-000000000003"),
        Guid.Parse("a0000001-0000-0000-0000-000000000004"),
        Guid.Parse("a0000001-0000-0000-0000-000000000005"),
        Guid.Parse("a0000001-0000-0000-0000-000000000006"),
        Guid.Parse("a0000001-0000-0000-0000-000000000007"),
        Guid.Parse("a0000001-0000-0000-0000-000000000008"),
        Guid.Parse("a0000001-0000-0000-0000-000000000009"),
        Guid.Parse("a0000001-0000-0000-0000-000000000010"),
    ];
}
