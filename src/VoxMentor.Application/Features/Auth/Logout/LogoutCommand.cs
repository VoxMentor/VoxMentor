using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Auth.Logout;

public record LogoutCommand(
    string? RefreshToken
) : IRequest<ApiResponse<object>>;
