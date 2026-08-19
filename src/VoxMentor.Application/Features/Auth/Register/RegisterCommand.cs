using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Auth.Register;

public record RegisterCommand(
    string FullName,
    string Email,
    string Password
) : IRequest<ApiResponse<RegisterResponseDto>>;
