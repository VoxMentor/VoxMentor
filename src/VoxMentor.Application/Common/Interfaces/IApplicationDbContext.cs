using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Abstraction over the EF Core context so Application-layer handlers and unit tests
/// can persist without referencing a concrete <c>DbContext</c>.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<ApplicationUser> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Concept> Concepts { get; }
    DbSet<Prerequisite> Prerequisites { get; }
    DbSet<Question> Questions { get; }
    DbSet<StudentMastery> StudentMasteries { get; }
    DbSet<CodeSubmission> CodeSubmissions { get; }
    DbSet<MockInterview> MockInterviews { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<BktParameters> BktParameters { get; }
    /// <summary>Persists tracked changes and returns the number of affected rows.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Detaches all tracked entities. Used to reset stale state before retrying
    /// after a concurrency conflict.
    /// </summary>
    void ClearChangeTracker();

    /// <summary>Gets the <see cref="EntityEntry{TEntity}"/> tracking the given entity.</summary>
    EntityEntry<T> Entry<T>(T entity) where T : class;
}
