using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Features.Admin.UploadTextbook;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Minimal Sqlite DbContext mirroring the ApplicationDbContext TextbookJob
/// binding. InMemory can't enforce unique indexes, so the DB-enforced side of
/// textbook dedup (#104 CodeRabbit) needs a relational provider. SQLite
/// supports the filtered partial unique index but not Npgsql's NULLS NOT
/// DISTINCT — the race test uses an explicit ConceptId (provider limitation,
/// not a gap; null-ConceptId dedup is PG-only and covered by the InMemory
/// race test's semantics).
/// </summary>
internal class TextbookDedupSqliteDbContext : DbContext, IApplicationDbContext
{
    public DbSet<Concept> Concepts => Set<Concept>();
    public DbSet<TextbookJob> TextbookJobs => Set<TextbookJob>();

    // Unused DbSets required by IApplicationDbContext — null-forgiven, never touched by these tests.
    public DbSet<ApplicationUser> Users => null!;
    public DbSet<RefreshToken> RefreshTokens => null!;
    public DbSet<Prerequisite> Prerequisites => null!;
    public DbSet<Question> Questions => null!;
    public DbSet<StudentMastery> StudentMasteries => null!;
    public DbSet<CodeSubmission> CodeSubmissions => null!;
    public DbSet<MockInterview> MockInterviews => null!;
    public DbSet<AuditLog> AuditLogs => null!;
    public DbSet<BktParameters> BktParameters => null!;
    public DbSet<JobDescription> JobDescriptions => null!;
    public DbSet<JdSkillWeight> JdSkillWeights => null!;
    public DbSet<TutorSession> TutorSessions => null!;
    public DbSet<TextbookChunk> TextbookChunks => null!;

    public TextbookDedupSqliteDbContext(DbContextOptions<TextbookDedupSqliteDbContext> options)
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
        builder.Ignore<TextbookChunk>();

        builder.Entity<Concept>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Category).IsRequired().HasMaxLength(100);
        });

        builder.Entity<TextbookJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
            // Mirror ApplicationDbContext's dedup rule — keep in sync.
            e.HasIndex(x => new { x.ContentHash, x.ConceptId })
                .IsUnique()
                .HasFilter("\"Status\" IN ('Pending', 'Processing', 'Completed') AND \"ContentHash\" <> ''");
        });
    }
}

/// <summary>
/// Executable proof of the "DB-enforced dedup" contract (#104 CodeRabbit):
/// a real unique index rejects the second reusable row, a Failed row escapes
/// the filter (fresh runs stay possible), and the handler converts a real
/// collision into the winning job's response instead of queueing a duplicate.
/// </summary>
public class UploadTextbookDedupSqliteTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public UploadTextbookDedupSqliteTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private static TextbookDedupSqliteDbContext CreateDb(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<TextbookDedupSqliteDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new TextbookDedupSqliteDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static string HashData(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static UploadTextbookCommand Command(string content, Guid conceptId)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new UploadTextbookCommand("race.pdf", new MemoryStream(bytes), bytes.LongLength, conceptId);
    }

    // Local copy of UploadTextbookHandlerTests.FakeQueue (file-private there).
    private sealed class FakeQueue : ITextbookIngestionQueue
    {
        public Guid JobId { get; private set; }
        public string? FilePath { get; private set; }

        public void Enqueue(Guid jobId, string filePath)
        {
            JobId = jobId;
            FilePath = filePath;
        }
    }

    /// <summary>
    /// Simulates the concurrent upload committing between the handler's lookup
    /// and its insert: seeds the winner through a sibling context, then lets
    /// the handler's own insert hit a REAL unique violation (no fabricated
    /// exception — proves the catch handles the actual DB error shape).
    /// </summary>
    private sealed class RaceOnSaveDbContext : TextbookDedupSqliteDbContext
    {
        private readonly DbContextOptions<TextbookDedupSqliteDbContext> _options;
        private readonly TextbookJob _winner;

        public RaceOnSaveDbContext(
            DbContextOptions<TextbookDedupSqliteDbContext> options, TextbookJob winner)
            : base(options)
        {
            _options = options;
            _winner = winner;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            {
                var winnerDb = new TextbookDedupSqliteDbContext(_options);
                winnerDb.TextbookJobs.Add(_winner);
                await winnerDb.SaveChangesAsync(cancellationToken);
            }
            return await base.SaveChangesAsync(cancellationToken); // real SQLITE_CONSTRAINT
        }
    }

    [Fact]
    public async Task TextbookJobs_DuplicateReusableKey_SecondInsert_Throws()
    {
        var db = CreateDb(_connection);
        var conceptId = Guid.NewGuid();
        var hash = HashData("duplicate conformance content");

        db.Concepts.Add(new Concept { Id = conceptId, Name = "Arrays", Category = "DSA" });
        db.TextbookJobs.Add(new TextbookJob
        {
            Id = Guid.NewGuid(), FileName = "a.pdf", ConceptId = conceptId,
            ContentHash = hash, Status = TextbookJobStatus.Pending
        });
        db.TextbookJobs.Add(new TextbookJob
        {
            Id = Guid.NewGuid(), FileName = "b.pdf", ConceptId = conceptId,
            ContentHash = hash, Status = TextbookJobStatus.Pending
        });

        // Index actually enforces: the second identical key fails to commit.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task TextbookJobs_FailedRowEscapesFilter_AllowsFreshRun()
    {
        var db = CreateDb(_connection);
        var conceptId = Guid.NewGuid();
        var hash = HashData("failed escape content");

        db.Concepts.Add(new Concept { Id = conceptId, Name = "Graphs", Category = "DSA" });
        db.TextbookJobs.Add(new TextbookJob
        {
            Id = Guid.NewGuid(), FileName = "a.pdf", ConceptId = conceptId,
            ContentHash = hash, Status = TextbookJobStatus.Failed
        });
        db.TextbookJobs.Add(new TextbookJob
        {
            Id = Guid.NewGuid(), FileName = "b.pdf", ConceptId = conceptId,
            ContentHash = hash, Status = TextbookJobStatus.Pending
        });

        // Failed = re-runnable: two rows, same (hash, concept), both commit.
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.TextbookJobs.CountAsync());
    }

    [Fact]
    public async Task Upload_RaceOnRealSqliteCollision_ReturnsWinnerId_AndSkipsEnqueue()
    {
        // Per-test Guid keeps the shared staging dir parallel-safe.
        var content = $"dedup race coverage {Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<TextbookDedupSqliteDbContext>()
            .UseSqlite(_connection)
            .Options;
        var seedDb = CreateDb(_connection);
        var conceptId = Guid.NewGuid();
        seedDb.Concepts.Add(new Concept { Id = conceptId, Name = "Arrays", Category = "DSA" });
        await seedDb.SaveChangesAsync();

        var stagingDir = Path.Combine(Path.GetTempPath(), ITextbookIngestionQueue.TempDirectoryName);
        var filesBefore = Directory.GetFiles(stagingDir).ToHashSet();

        var winnerId = Guid.NewGuid();
        var raceDb = new RaceOnSaveDbContext(options, new TextbookJob
        {
            Id = winnerId,
            FileName = "race.pdf",
            ConceptId = conceptId,
            ContentHash = HashData(content),
            Status = TextbookJobStatus.Pending
        });
        var queue = new FakeQueue();
        var handler = new UploadTextbookHandler(raceDb, queue);

        var result = await handler.Handle(Command(content, conceptId), CancellationToken.None);

        Assert.Equal(winnerId, result.Data!.JobId);
        Assert.Contains("already running", result.Data!.Message);
        Assert.Equal(Guid.Empty, queue.JobId); // the losing duplicate is never queued
        Assert.Equal(1, await raceDb.TextbookJobs.CountAsync()); // only the winner persisted

        // The handler deleted its own staged duplicate (ReuseResponse).
        Assert.Empty(Directory.GetFiles(stagingDir).Except(filesBefore));
    }
}
