using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.KnowledgeGraph.GetEligibleConcepts;
using VoxMentor.Application.Features.KnowledgeGraph.GetPrerequisiteChain;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Minimal Sqlite DbContext for knowledge-graph raw-SQL tests. Only the three
/// tables the recursive-CTE queries touch; InMemory can't execute CTEs.
/// </summary>
internal sealed class KnowledgeGraphTestDbContext : DbContext, IApplicationDbContext
{
    public DbSet<Concept> Concepts => Set<Concept>();
    public DbSet<Prerequisite> Prerequisites => Set<Prerequisite>();
    public DbSet<StudentMastery> StudentMasteries => Set<StudentMastery>();

    // Unused DbSets required by IApplicationDbContext — null-forgiven, never touched by these tests.
    public DbSet<ApplicationUser> Users => null!;
    public DbSet<RefreshToken> RefreshTokens => null!;
    public DbSet<Question> Questions => null!;
    public DbSet<CodeSubmission> CodeSubmissions => null!;
    public DbSet<MockInterview> MockInterviews => null!;
    public DbSet<AuditLog> AuditLogs => null!;
    public DbSet<BktParameters> BktParameters => null!;
    public DbSet<JobDescription> JobDescriptions => null!;
    public DbSet<JdSkillWeight> JdSkillWeights => null!;
    public DbSet<TutorSession> TutorSessions => null!;

    public KnowledgeGraphTestDbContext(DbContextOptions<KnowledgeGraphTestDbContext> options)
        : base(options)
    {
    }

    public void ClearChangeTracker() => ChangeTracker.Clear();

    public IQueryable<T> SqlQueryRaw<T>(string sql, params object[] parameters) where T : class
        => Database.SqlQueryRaw<T>(sql, parameters);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // ponytail: null! DbSets still register entity types — ignore everything unused
        // so Vector/Identity/etc. don't need full configuration in this minimal context.
        builder.Ignore<ApplicationUser>();
        builder.Ignore<RefreshToken>();
        builder.Ignore<Question>();
        builder.Ignore<CodeSubmission>();
        builder.Ignore<MockInterview>();
        builder.Ignore<AuditLog>();
        builder.Ignore<BktParameters>();
        builder.Ignore<JobDescription>();
        builder.Ignore<JdSkillWeight>();
        builder.Ignore<TutorSession>();

        builder.Entity<Concept>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Category).IsRequired().HasMaxLength(100);
        });

        builder.Entity<Prerequisite>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ConceptId, x.RequiredConceptId }).IsUnique();
        });

        builder.Entity<StudentMastery>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.ConceptId }).IsUnique();
            e.Property(x => x.RowVersion).ValueGeneratedNever();
        });
    }
}

/// <summary>
/// Coverage matrix for GetPrerequisiteChainHandler and GetEligibleConceptsHandler:
/// eligible / almost-eligible / locked / vacuous / self-mastered / chain depth / unauthenticated.
/// </summary>
public class KnowledgeGraphQueriesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private const string UserId = "student-1";
    private const float Mastered = 0.9f;
    private const float NotMastered = 0.5f;

    // Graph: root → mid → leaf; side is root-only; lonely has no prereqs;
    // locked needs root+mid (2 unmet) → neither bucket.
    private static readonly Guid Root = Guid.Parse("a0000001-0000-0000-0000-000000000001");
    private static readonly Guid Mid = Guid.Parse("a0000001-0000-0000-0000-000000000002");
    private static readonly Guid Leaf = Guid.Parse("a0000001-0000-0000-0000-000000000003");
    private static readonly Guid Side = Guid.Parse("a0000001-0000-0000-0000-000000000004");
    private static readonly Guid Lonely = Guid.Parse("a0000001-0000-0000-0000-000000000005");
    private static readonly Guid Locked = Guid.Parse("a0000001-0000-0000-0000-000000000006");

    public KnowledgeGraphQueriesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private KnowledgeGraphTestDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<KnowledgeGraphTestDbContext>()
            .UseSqlite(_connection)
            .Options;
        var db = new KnowledgeGraphTestDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static void SeedGraph(KnowledgeGraphTestDbContext db)
    {
        db.Concepts.AddRange(
            new Concept { Id = Root, Name = "Root", Category = "Basics", DifficultyLevel = 1 },
            new Concept { Id = Mid, Name = "Mid", Category = "Basics", DifficultyLevel = 2 },
            new Concept { Id = Leaf, Name = "Leaf", Category = "Advanced", DifficultyLevel = 3 },
            new Concept { Id = Side, Name = "Side", Category = "Basics", DifficultyLevel = 2 },
            new Concept { Id = Lonely, Name = "Lonely", Category = "Misc", DifficultyLevel = 1 },
            new Concept { Id = Locked, Name = "Locked", Category = "Advanced", DifficultyLevel = 4 });

        db.Prerequisites.AddRange(
            new Prerequisite { Id = Guid.NewGuid(), ConceptId = Mid, RequiredConceptId = Root },
            new Prerequisite { Id = Guid.NewGuid(), ConceptId = Leaf, RequiredConceptId = Mid },
            new Prerequisite { Id = Guid.NewGuid(), ConceptId = Side, RequiredConceptId = Root },
            new Prerequisite { Id = Guid.NewGuid(), ConceptId = Locked, RequiredConceptId = Root },
            new Prerequisite { Id = Guid.NewGuid(), ConceptId = Locked, RequiredConceptId = Mid });

        db.SaveChanges();
    }

    private static void SeedMastery(KnowledgeGraphTestDbContext db, Guid conceptId, float probability)
    {
        db.StudentMasteries.Add(new StudentMastery
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            ConceptId = conceptId,
            MasteryProbability = probability
        });
        db.SaveChanges();
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public string? UserId { get; init; }
    }

    // ---- GetPrerequisiteChain ----

    [Fact]
    public async Task Chain_ThreeDeep_ReturnsAllPrerequisitesByDepth()
    {
        await using var db = CreateDb();
        SeedGraph(db);

        var handler = new GetPrerequisiteChainHandler(db);
        var result = await handler.Handle(
            new GetPrerequisiteChainQuery { ConceptId = Leaf }, CancellationToken.None);

        Assert.True(result.Success);
        var items = result.Data!;
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.ConceptId == Mid && i.Depth == 1);
        Assert.Contains(items, i => i.ConceptId == Root && i.Depth == 2);
    }

    [Fact]
    public async Task Chain_RootHasNoPrerequisites_ReturnsEmpty()
    {
        await using var db = CreateDb();
        SeedGraph(db);

        var handler = new GetPrerequisiteChainHandler(db);
        var result = await handler.Handle(
            new GetPrerequisiteChainQuery { ConceptId = Root }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    // ---- GetEligibleConcepts ----

    [Fact]
    public async Task Eligible_VacuousZeroPrereqs_IncludesLonely()
    {
        await using var db = CreateDb();
        SeedGraph(db);

        var handler = new GetEligibleConceptsHandler(db, new FakeCurrentUser { UserId = UserId });
        var result = await handler.Handle(new GetEligibleConceptsQuery(), CancellationToken.None);

        Assert.True(result.Success);
        // Root has no prereqs (vacuous), Lonely has no prereqs (vacuous);
        // Mid/Side need Root, Leaf needs Mid — none mastered yet.
        Assert.Contains(result.Data!.Eligible, c => c.ConceptId == Root);
        Assert.Contains(result.Data.Eligible, c => c.ConceptId == Lonely);
        Assert.DoesNotContain(result.Data.Eligible, c => c.ConceptId == Leaf);
    }

    [Fact]
    public async Task Eligible_AllPrereqsMastered_UnlocksMid()
    {
        await using var db = CreateDb();
        SeedGraph(db);
        SeedMastery(db, Root, Mastered);

        var handler = new GetEligibleConceptsHandler(db, new FakeCurrentUser { UserId = UserId });
        var result = await handler.Handle(new GetEligibleConceptsQuery(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Data!.Eligible, c => c.ConceptId == Mid);
        Assert.Contains(result.Data.Eligible, c => c.ConceptId == Side);
        // Leaf still needs Mid.
        Assert.DoesNotContain(result.Data.Eligible, c => c.ConceptId == Leaf);
    }

    [Fact]
    public async Task AlmostEligible_OnePrereqMissing_IsAlmost()
    {
        await using var db = CreateDb();
        SeedGraph(db);
        // Mid needs Root (unmet) → almost; Side same; Leaf needs Mid → almost.
        // Root/Lonely vacuous → eligible.

        var handler = new GetEligibleConceptsHandler(db, new FakeCurrentUser { UserId = UserId });
        var result = await handler.Handle(new GetEligibleConceptsQuery(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Data!.AlmostEligible, c => c.ConceptId == Mid);
        Assert.Contains(result.Data.AlmostEligible, c => c.ConceptId == Side);
        Assert.Contains(result.Data.AlmostEligible, c => c.ConceptId == Leaf);
        Assert.DoesNotContain(result.Data.Eligible, c => c.ConceptId == Mid);
    }

    [Fact]
    public async Task Eligible_TwoUnmetPrereqs_LockedInNeitherBucket()
    {
        await using var db = CreateDb();
        SeedGraph(db);
        // Locked needs Root + Mid; neither mastered → missing=2 → not almost, not eligible.

        var handler = new GetEligibleConceptsHandler(db, new FakeCurrentUser { UserId = UserId });
        var result = await handler.Handle(new GetEligibleConceptsQuery(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Data!.Eligible, c => c.ConceptId == Locked);
        Assert.DoesNotContain(result.Data.AlmostEligible, c => c.ConceptId == Locked);
    }

    [Fact]
    public async Task Eligible_SelfMastered_IsExcluded()
    {
        await using var db = CreateDb();
        SeedGraph(db);
        SeedMastery(db, Root, Mastered);
        SeedMastery(db, Mid, Mastered);

        var handler = new GetEligibleConceptsHandler(db, new FakeCurrentUser { UserId = UserId });
        var result = await handler.Handle(new GetEligibleConceptsQuery(), CancellationToken.None);

        Assert.True(result.Success);
        // Mid is mastered → excluded from both buckets even though all its prereqs are met.
        Assert.DoesNotContain(result.Data!.Eligible, c => c.ConceptId == Mid);
        Assert.DoesNotContain(result.Data.AlmostEligible, c => c.ConceptId == Mid);
    }

    [Fact]
    public async Task Eligible_Unauthenticated_Throws()
    {
        await using var db = CreateDb();
        SeedGraph(db);

        var handler = new GetEligibleConceptsHandler(db, new FakeCurrentUser { UserId = null });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new GetEligibleConceptsQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task Eligible_MasteryBelowThreshold_DoesNotCountAsMastered()
    {
        await using var db = CreateDb();
        SeedGraph(db);
        SeedMastery(db, Root, NotMastered); // below MasteredThreshold

        var handler = new GetEligibleConceptsHandler(db, new FakeCurrentUser { UserId = UserId });
        var result = await handler.Handle(new GetEligibleConceptsQuery(), CancellationToken.None);

        Assert.True(result.Success);
        // Root not mastered → Mid/Side not eligible; Root itself still eligible (vacuous, self not mastered).
        Assert.DoesNotContain(result.Data!.Eligible, c => c.ConceptId == Mid);
        Assert.Contains(result.Data.Eligible, c => c.ConceptId == Root);
    }
}
