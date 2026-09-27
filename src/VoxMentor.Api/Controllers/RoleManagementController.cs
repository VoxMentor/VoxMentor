using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using VoxMentor.Api.Authorization;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;
using VoxMentor.Infrastructure.Persistence;

namespace VoxMentor.Api.Controllers;

public record AssignRoleRequest(string Role);

public record UserRolesDto(string Id, string? Email, string FullName, IList<string> Roles);

/// <summary>
/// Role management APIs — SuperAdmin only via the ManageRoles policy (#82).
/// Guard: the last SuperAdmin can never be removed (self-demotion included).
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = Policies.ManageRoles)]
// ponytail: direct Identity services instead of MediatR — thin CRUD, no domain logic to isolate.
public class RoleManagementController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public RoleManagementController(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ApplicationDbContext db,
        ICurrentUserService currentUser)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>Lists all role names.</summary>
    [HttpGet("roles")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public IActionResult GetRoles()
    {
        var roles = _roleManager.Roles
            .Select(r => r.Name ?? string.Empty)
            .Where(n => n.Length > 0)
            .OrderBy(n => n)
            .ToList();
        return Ok(ApiResponse<IReadOnlyList<string>>.SuccessResult(roles));
    }

    /// <summary>Lists users paged with their roles.</summary>
    [HttpGet("users")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserRolesDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        // mirror GetQuestionsHandler: offset as long, empty page past int.MaxValue
        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue)
        {
            return Ok(ApiResponse<IReadOnlyList<UserRolesDto>>.SuccessResult(new List<UserRolesDto>()));
        }

        var users = _userManager.Users
            .OrderBy(u => u.Email)
            .Skip((int)offset)
            .Take(pageSize)
            .ToList();

        var result = new List<UserRolesDto>(users.Count);
        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(new UserRolesDto(
                user.Id,
                user.Email,
                user.FullName,
                await _userManager.GetRolesAsync(user)));
        }

        return Ok(ApiResponse<IReadOnlyList<UserRolesDto>>.SuccessResult(result));
    }

    /// <summary>Assigns an existing role to a user.</summary>
    [HttpPost("users/{userId}/roles")]
    [ProducesResponseType(typeof(ApiResponse<UserRolesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignRole(string userId, [FromBody] AssignRoleRequest request)
    {
        var denied = await EnsureCallerSuperAdminAsync();
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(request.Role))
        {
            return BadRequest(ApiResponse<object>.FailureResult("Role is required."));
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return NotFound(ApiResponse<object>.FailureResult("User not found."));
        }

        if (!await _roleManager.RoleExistsAsync(request.Role))
        {
            return BadRequest(ApiResponse<object>.FailureResult($"Role '{request.Role}' does not exist."));
        }

        var result = await _userManager.AddToRoleAsync(user, request.Role);
        if (!result.Succeeded)
        {
            return BadRequest(ApiResponse<object>.FailureResult("Failed to assign role.", ToErrors(result)));
        }

        var dto = new UserRolesDto(user.Id, user.Email, user.FullName, await _userManager.GetRolesAsync(user));
        return Ok(ApiResponse<UserRolesDto>.SuccessResult(dto, "Role assigned."));
    }

    /// <summary>Removes a role from a user. Never removes the last SuperAdmin.</summary>
    [HttpDelete("users/{userId}/roles/{role}")]
    [ProducesResponseType(typeof(ApiResponse<UserRolesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveRole(string userId, string role)
    {
        var denied = await EnsureCallerSuperAdminAsync();
        if (denied is not null)
        {
            return denied;
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return NotFound(ApiResponse<object>.FailureResult("User not found."));
        }

        if (!await _roleManager.RoleExistsAsync(role))
        {
            return BadRequest(ApiResponse<object>.FailureResult($"Role '{role}' does not exist."));
        }

        // Last-SuperAdmin guard (#82) — case-insensitive so /roles/superadmin can't slip past it.
        // Serializable transaction closes the check-then-act race: two concurrent removals
        // of distinct SuperAdmins can no longer both pass the count.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var transaction = await BeginGuardTransactionAsync();

                if (string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                {
                    var targetHoldsRole = (await _userManager.GetRolesAsync(user))
                        .Any(r => string.Equals(r, "SuperAdmin", StringComparison.OrdinalIgnoreCase));
                    if (targetHoldsRole)
                    {
                        var superAdmins = await _userManager.GetUsersInRoleAsync("SuperAdmin");
                        if (superAdmins.Count <= 1)
                        {
                            // disposed without commit → rollback
                            return BadRequest(ApiResponse<object>.FailureResult("Cannot remove the last SuperAdmin."));
                        }
                    }
                }

                var result = await _userManager.RemoveFromRoleAsync(user, role);
                if (!result.Succeeded)
                {
                    return BadRequest(ApiResponse<object>.FailureResult("Failed to remove role.", ToErrors(result)));
                }

                if (transaction is not null)
                {
                    await transaction.CommitAsync();
                }
                break;
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                if (attempt >= 1)
                {
                    return Conflict(ApiResponse<object>.FailureResult(
                        "Concurrent role update detected; please retry."));
                }
                _db.ChangeTracker.Clear();
            }
        }

        var dto = new UserRolesDto(user.Id, user.Email, user.FullName, await _userManager.GetRolesAsync(user));
        return Ok(ApiResponse<UserRolesDto>.SuccessResult(dto, "Role removed."));
    }

    /// <summary>
    /// Defense-in-depth (#82): role mutations re-check the caller's DB roles so a
    /// stale SuperAdmin claim can't change roles after demotion. Self-demotion is
    /// still allowed while the DB still grants the role.
    /// </summary>
    private async Task<IActionResult?> EnsureCallerSuperAdminAsync()
    {
        var callerId = _currentUser.UserId;
        var caller = callerId is null ? null : await _userManager.FindByIdAsync(callerId);
        if (caller is null)
        {
            return Unauthorized(ApiResponse<object>.FailureResult("Session is no longer valid."));
        }

        var roles = await _userManager.GetRolesAsync(caller);
        if (!roles.Contains("SuperAdmin", StringComparer.OrdinalIgnoreCase))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<object>.FailureResult("Role grants have changed; re-authenticate."));
        }
        return null;
    }

    /// <summary>
    /// Serializable transaction for the last-SuperAdmin guard. Returns null on
    /// non-relational providers (EF InMemory tests have no transactions).
    /// </summary>
    private async Task<IDbContextTransaction?> BeginGuardTransactionAsync()
    {
        if (!_db.Database.IsRelational())
        {
            return null;
        }
        return await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    }

    private static bool IsSerializationFailure(Exception ex)
    {
        // Postgres serialization failures: 40001 = serialization, 40P01 = deadlock.
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is PostgresException pg && (pg.SqlState == "40001" || pg.SqlState == "40P01"))
            {
                return true;
            }
        }
        return false;
    }

    private static Dictionary<string, string[]> ToErrors(IdentityResult result)
    {
        return result.Errors
            .GroupBy(e => e.Code ?? "Role")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
    }
}
