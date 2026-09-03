using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Infrastructure.Persistence;

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

    public void ClearChangeTracker() => ChangeTracker.Clear();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

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
    }
}
