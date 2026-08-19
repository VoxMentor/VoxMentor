using MediatR;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Auth.Login;

namespace VoxMentor.Application.Features.Auth.RefreshToken;

public record RefreshTokenCommand(
    string RefreshToken
) : IRequest<ApiResponse<LoginResultDto>>;
