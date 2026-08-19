namespace VoxMentor.Infrastructure.Authentication;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string Secret { get; init; } = "VoxMentorSuperSecretSecurityKey2026!KeyLengthMin256Bits";
    public string Issuer { get; init; } = "VoxMentorApi";
    public string Audience { get; init; } = "VoxMentorApp";
    public int AccessTokenExpirationMinutes { get; init; } = 15;
    public int RefreshTokenExpirationDays { get; init; } = 7;
}
