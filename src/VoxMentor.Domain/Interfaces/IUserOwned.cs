namespace VoxMentor.Domain.Interfaces;

/// <summary>
/// Marks an entity as owned by a single user. ApplicationDbContext applies a
/// global EF query filter on every <see cref="IUserOwned"/> entity so queries
/// default to the current user's rows (tenant isolation, #57).
/// Cross-user paths (background jobs, ops controllers, login token revocation)
/// must opt out explicitly with IgnoreQueryFilters().
/// </summary>
public interface IUserOwned
{
    /// <summary>The owning user's Identity id.</summary>
    string UserId { get; set; }
}
