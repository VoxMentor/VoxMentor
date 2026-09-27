using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.Api.Authorization;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;

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

    public RoleManagementController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
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

        var users = _userManager.Users
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
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
        if (string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            var targetHoldsRole = (await _userManager.GetRolesAsync(user))
                .Any(r => string.Equals(r, "SuperAdmin", StringComparison.OrdinalIgnoreCase));
            if (targetHoldsRole)
            {
                var superAdmins = await _userManager.GetUsersInRoleAsync("SuperAdmin");
                if (superAdmins.Count <= 1)
                {
                    return BadRequest(ApiResponse<object>.FailureResult("Cannot remove the last SuperAdmin."));
                }
            }
        }

        var result = await _userManager.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
        {
            return BadRequest(ApiResponse<object>.FailureResult("Failed to remove role.", ToErrors(result)));
        }

        var dto = new UserRolesDto(user.Id, user.Email, user.FullName, await _userManager.GetRolesAsync(user));
        return Ok(ApiResponse<UserRolesDto>.SuccessResult(dto, "Role removed."));
    }

    private static Dictionary<string, string[]> ToErrors(IdentityResult result)
    {
        return result.Errors
            .GroupBy(e => e.Code ?? "Role")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
    }
}
