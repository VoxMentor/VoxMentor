using MediatR;
using Microsoft.AspNetCore.Identity;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Models;
using VoxMentor.Domain.Entities;

namespace VoxMentor.Application.Features.Auth.Register;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, ApiResponse<RegisterResponseDto>>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public RegisterCommandHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<ApiResponse<RegisterResponseDto>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
        {
            throw new ConflictException("A user with this email address is already registered.");
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = createResult.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

            if (createResult.Errors.Any(e => e.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ConflictException("A user with this email address is already registered.");
            }

            throw new ValidationException(errors);
        }

        const string assignedRole = "Student";
        var roleResult = await _userManager.AddToRoleAsync(user, assignedRole);
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            var errors = roleResult.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
            throw new ValidationException(errors);
        }

        var responseDto = new RegisterResponseDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Role = assignedRole,
            CreatedAt = user.CreatedAt
        };

        return ApiResponse<RegisterResponseDto>.SuccessResult(responseDto, "User registered successfully.");
    }
}
