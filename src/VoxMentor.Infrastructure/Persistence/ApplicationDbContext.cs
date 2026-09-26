using System.Text.Json;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Infrastructure.Persistence;

/// <summary>
/// EF Core context for all VoxMentor entities, including Identity stores.
/// Configures keys, unique indexes, and concurrency tokens.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<Concept> Concepts { get; set; } = null!;
    public DbSet<Prerequisite> Prerequisites { get; set; } = null!;
    public DbSet<Question> Questions { get; set; } = null!;
    public DbSet<StudentMastery> StudentMasteries { get; set; } = null!;
    public DbSet<CodeSubmission> CodeSubmissions { get; set; } = null!;
    public DbSet<MockInterview> MockInterviews { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<BktParameters> BktParameters { get; set; } = null!;
    public DbSet<JobDescription> JobDescriptions { get; set; } = null!;
    public DbSet<JdSkillWeight> JdSkillWeights { get; set; } = null!;
    public DbSet<TutorSession> TutorSessions { get; set; } = null!;
    public DbSet<TextbookJob> TextbookJobs { get; set; } = null!;
    public DbSet<TextbookChunk> TextbookChunks { get; set; } = null!;

    /// <inheritdoc />
    public void ClearChangeTracker() => ChangeTracker.Clear();

    /// <inheritdoc />
    public IQueryable<T> SqlQueryRaw<T>(string sql, params object[] parameters) where T : class
        => Database.SqlQueryRaw<T>(sql, parameters);

    /// <summary>
    /// Configures entity keys, constraints, and the StudentMastery xmin
    /// concurrency token.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresExtension("vector");

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.FullName)
                .IsRequired()
                .HasMaxLength(100);
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).IsRequired();
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.Property(e => e.Version).IsConcurrencyToken();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Concept>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Category).IsRequired().HasMaxLength(100);
        });

        builder.Entity<Prerequisite>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ConceptId, e.RequiredConceptId }).IsUnique();
        });

        builder.Entity<Question>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(300);
            entity.Property(e => e.QuestionType).IsRequired().HasMaxLength(50).HasDefaultValue("Code");
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Questions_HiddenTestCaseCount_NonNegative", "\"HiddenTestCaseCount\" >= 0");
                t.HasCheckConstraint("CK_Questions_HiddenTestCaseCount_UpperBound", "\"HiddenTestCaseCount\" <= cardinality(\"TestCases\")");
            });
        });

        builder.Entity<StudentMastery>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.ConceptId }).IsUnique();
            entity.Property(e => e.RowVersion).IsRowVersion();
        });

        builder.Entity<CodeSubmission>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Write-once claim: concurrency token so a stale duplicate save fails
            // (WHERE MasteryAppliedAt IS NULL) instead of double-applying mastery.
            entity.Property(e => e.MasteryAppliedAt).IsConcurrencyToken();
            entity.Property(e => e.CodeEmbedding)
                .HasColumnType("vector(768)")
                .HasConversion<VectorToJsonConverter>();
            entity.HasIndex(e => e.CodeEmbedding)
                .HasDatabaseName("IX_CodeSubmissions_CodeEmbedding")
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });

        builder.Entity<MockInterview>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
        });

        builder.Entity<BktParameters>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ConceptId).IsUnique();
        });

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

        builder.Entity<TutorSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.Question).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
        });

        builder.Entity<TextbookJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(260);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Error).HasMaxLength(1000);
        });

        builder.Entity<TextbookChunk>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Source).IsRequired().HasMaxLength(260);
            entity.Property(e => e.Embedding)
                .HasColumnType("vector(768)");
            // InMemory (tests) can't map Pgvector's Vector → string converter there.
            // Npgsql maps Vector natively via UseVector(); converter on Npgsql would
            // send varchar against a vector column (42804).
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                entity.Property(e => e.Embedding).HasConversion<VectorToJsonConverter>();
            }
            entity.HasIndex(e => e.JobId);
            entity.HasIndex(e => e.ConceptId);
            // ponytail: ivfflat per issue #70 spec; on the empty load table its
            // centroids are untrained — REINDEX after #75 bulk-seeds if recall degrades.
            entity.HasIndex(e => e.Embedding)
                .HasDatabaseName("IX_TextbookChunks_Embedding")
                .HasMethod("ivfflat")
                .HasOperators("vector_cosine_ops");
        });
    }
}

/// <summary>
/// Converts Pgvector Vector to/from JSON string for EF Core storage.
/// ponytail: avoids expression-tree issues with optional args by using a concrete converter class.
/// </summary>
public class VectorToJsonConverter : ValueConverter<Vector?, string?>
{
    public VectorToJsonConverter()
        : base(v => VectorToJson(v), s => JsonToVector(s))
    {
    }

    private static string? VectorToJson(Vector? v)
    {
        if (v is null) return null;
        var arr = new float[v.Memory.Span.Length];
        v.Memory.Span.CopyTo(arr);
        return JsonSerializer.Serialize(arr);
    }

    private static Vector? JsonToVector(string? s)
    {
        if (s is null) return null;
        return new Vector(JsonSerializer.Deserialize<float[]>(s)!);
    }
}
