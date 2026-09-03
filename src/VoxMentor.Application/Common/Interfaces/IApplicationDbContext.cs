using Microsoft.EntityFrameworkCore;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Common.Interfaces;

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
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    void ClearChangeTracker();
}
