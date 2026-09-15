using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Minimal DbContext containing only JobDescription + JdSkillWeight so
/// SQLite EnsureCreated() doesn't hit PostgreSQL-specific constraints
/// on other tables (e.g. Questions cardinality check).
/// </summary>
internal sealed class JdTestDbContext : DbContext
{
    public DbSet<JobDescription> JobDescriptions => Set<JobDescription>();
    public DbSet<JdSkillWeight> JdSkillWeights => Set<JdSkillWeight>();

    public JdTestDbContext(DbContextOptions<JdTestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<JobDescription>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.CompanyName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Role).IsRequired().HasMaxLength(200);
            entity.Property(e => e.RawText).IsRequired();
            entity.Property(e => e.Difficulty).HasMaxLength(50);
        });

        builder.Entity<JdSkillWeight>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.JobDescriptionId, e.SkillName }).IsUnique();
            entity.Property(e => e.SkillName).IsRequired().HasMaxLength(100);
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_JdSkillWeights_Weight_Range",
                "\"Weight\" >= 0 AND \"Weight\" <= 1"));

            entity.HasOne<JobDescription>()
                .WithMany()
                .HasForeignKey(e => e.JobDescriptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>
/// Unit tests for <see cref="JobDescription"/> and <see cref="JdSkillWeight"/>
/// persistence, FK constraints, unique composite index, cascade delete,
/// and Weight range constraint — using SQLite in-memory for relational enforcement.
/// </summary>
public class JobDescriptionTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public JobDescriptionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private JdTestDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<JdTestDbContext>()
            .UseSqlite(_connection)
            .Options;
        var db = new JdTestDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task JobDescription_CanBePersisted()
    {
        await using var db = CreateDb();
        var jd = new JobDescription
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            CompanyName = "Amazon",
            Role = "SDE-1",
            RawText = "Amazon is hiring an SDE-1...",
            Difficulty = "Medium-Hard",
            EstimatedWeeks = 6
        };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();

        var loaded = await db.JobDescriptions.FindAsync(jd.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Amazon", loaded.CompanyName);
        Assert.Equal("SDE-1", loaded.Role);
        Assert.Equal(6, loaded.EstimatedWeeks);
    }

    [Fact]
    public async Task JdSkillWeight_CanBePersisted()
    {
        await using var db = CreateDb();
        var jd = new JobDescription { Id = Guid.NewGuid(), UserId = "user-1", CompanyName = "Google", Role = "SWE", RawText = "text" };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();

        var skill = new JdSkillWeight
        {
            Id = Guid.NewGuid(),
            JobDescriptionId = jd.Id,
            SkillName = "DP",
            Weight = 0.35f,
            IsTechnical = true
        };
        db.JdSkillWeights.Add(skill);
        await db.SaveChangesAsync();

        var loaded = await db.JdSkillWeights.FindAsync(skill.Id);
        Assert.NotNull(loaded);
        Assert.Equal("DP", loaded.SkillName);
        Assert.Equal(0.35f, loaded.Weight);
        Assert.True(loaded.IsTechnical);
    }

    [Fact]
    public async Task JdSkillWeight_UniqueCompositeIndex_ThrowsOnDuplicate()
    {
        await using var db = CreateDb();
        var jd = new JobDescription { Id = Guid.NewGuid(), UserId = "user-1", CompanyName = "X", Role = "Y", RawText = "t" };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();

        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd.Id, SkillName = "DP", Weight = 0.3f });
        await db.SaveChangesAsync();

        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd.Id, SkillName = "DP", Weight = 0.5f });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task JobDescription_CascadeDelete_RemovesSkillWeights()
    {
        await using var db = CreateDb();
        var jd = new JobDescription { Id = Guid.NewGuid(), UserId = "user-1", CompanyName = "X", Role = "Y", RawText = "t" };
        db.JobDescriptions.Add(jd);
        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd.Id, SkillName = "DP", Weight = 0.3f });
        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd.Id, SkillName = "Graphs", Weight = 0.2f });
        await db.SaveChangesAsync();

        db.JobDescriptions.Remove(jd);
        await db.SaveChangesAsync();

        Assert.Empty(await db.JdSkillWeights.Where(s => s.JobDescriptionId == jd.Id).ToListAsync());
    }

    [Fact]
    public async Task JdSkillWeight_ForeignKeyConstraint_ThrowsOnInvalidJobDescriptionId()
    {
        await using var db = CreateDb();
        var skill = new JdSkillWeight
        {
            Id = Guid.NewGuid(),
            JobDescriptionId = Guid.NewGuid(),
            SkillName = "DP",
            Weight = 0.3f
        };
        db.JdSkillWeights.Add(skill);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task JdSkillWeight_DifferentSkillsSameJd_Allowed()
    {
        await using var db = CreateDb();
        var jd = new JobDescription { Id = Guid.NewGuid(), UserId = "user-1", CompanyName = "X", Role = "Y", RawText = "t" };
        db.JobDescriptions.Add(jd);
        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd.Id, SkillName = "DP", Weight = 0.3f });
        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd.Id, SkillName = "Graphs", Weight = 0.2f });
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.JdSkillWeights.CountAsync(s => s.JobDescriptionId == jd.Id));
    }

    [Fact]
    public async Task JdSkillWeight_SameSkillDifferentJds_Allowed()
    {
        await using var db = CreateDb();
        var jd1 = new JobDescription { Id = Guid.NewGuid(), UserId = "user-1", CompanyName = "A", Role = "X", RawText = "t" };
        var jd2 = new JobDescription { Id = Guid.NewGuid(), UserId = "user-1", CompanyName = "B", Role = "Y", RawText = "t" };
        db.JobDescriptions.AddRange(jd1, jd2);
        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd1.Id, SkillName = "DP", Weight = 0.3f });
        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd2.Id, SkillName = "DP", Weight = 0.5f });
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.JdSkillWeights.CountAsync(s => s.SkillName == "DP"));
    }

    [Fact]
    public async Task JdSkillWeight_WeightBelowZero_ThrowsCheckConstraint()
    {
        await using var db = CreateDb();
        var jd = new JobDescription { Id = Guid.NewGuid(), UserId = "user-1", CompanyName = "X", Role = "Y", RawText = "t" };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();

        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd.Id, SkillName = "DP", Weight = -0.1f });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task JdSkillWeight_WeightAboveOne_ThrowsCheckConstraint()
    {
        await using var db = CreateDb();
        var jd = new JobDescription { Id = Guid.NewGuid(), UserId = "user-1", CompanyName = "X", Role = "Y", RawText = "t" };
        db.JobDescriptions.Add(jd);
        await db.SaveChangesAsync();

        db.JdSkillWeights.Add(new JdSkillWeight { Id = Guid.NewGuid(), JobDescriptionId = jd.Id, SkillName = "DP", Weight = 1.5f });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
