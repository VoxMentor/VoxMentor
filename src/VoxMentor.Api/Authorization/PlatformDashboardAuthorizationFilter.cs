using Hangfire.Dashboard;

namespace VoxMentor.Api.Authorization;

/// <summary>Gates the Hangfire dashboard to PlatformAdmin/SuperAdmin (issue #82).</summary>
public class PlatformDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;
        return user.IsInRole("PlatformAdmin") || user.IsInRole("SuperAdmin");
    }
}
