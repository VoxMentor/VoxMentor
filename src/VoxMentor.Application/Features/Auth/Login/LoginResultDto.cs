namespace VoxMentor.Application.Features.Auth.Login;

public class LoginResultDto
{
    public LoginResponseDto User { get; set; } = new();
    public string AccessToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiration { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset RefreshTokenExpiration { get; set; }
}
