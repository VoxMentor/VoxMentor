using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Auth.Login;

public record LoginCommand(
    string Email,
    string Password
) : IRequest<ApiResponse<LoginResultDto>>
{
    public override string ToString() => $"LoginCommand {{ Email = {Email}, Password = *** }}";
}
