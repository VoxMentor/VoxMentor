using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Domain.Entities;
using VoxMentor.Infrastructure.Authentication;
using VoxMentor.Infrastructure.Persistence;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var rawConnectionString = configuration.GetConnectionString("DefaultConnection");
        var connectionString = ParseConnectionString(rawConnectionString);

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 8;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders()
        .AddSignInManager();

        var jwtSettings = new JwtSettings();
        configuration.Bind(JwtSettings.SectionName, jwtSettings);
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        if (string.IsNullOrWhiteSpace(jwtSettings.Secret) ||
            Encoding.UTF8.GetByteCount(jwtSettings.Secret) < 32)
        {
            throw new InvalidOperationException(
                "JwtSettings:Secret must be configured via deployment secret storage and be at least 32 bytes long.");
        }

        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IRefreshTokenHasher, RefreshTokenHasher>();
        services.AddScoped<IHealthService, HealthService>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IMasteryEventPublisher, NullMasteryEventPublisher>();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret))
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (context.Request.Cookies.TryGetValue("access_token", out var token))
                    {
                        context.Token = token;
                    }
                    return Task.CompletedTask;
                }
            };
        });

        return services;
    }

    private static string ParseConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The 'ConnectionStrings:DefaultConnection' configuration value is missing. Provide a valid PostgreSQL connection string via deployment configuration.");
        }

        if (connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var schemeEnd = connectionString.IndexOf("://", StringComparison.Ordinal) + 3;
                var pathStart = connectionString.IndexOf('/', schemeEnd);
                var userHostPart = pathStart > 0 ? connectionString.Substring(schemeEnd, pathStart - schemeEnd) : connectionString.Substring(schemeEnd);
                var dbName = pathStart > 0 ? connectionString.Substring(pathStart + 1) : "postgres";

                var lastAtIndex = userHostPart.LastIndexOf('@');
                if (lastAtIndex > 0)
                {
                    var userPass = userHostPart.Substring(0, lastAtIndex);
                    var hostPort = userHostPart.Substring(lastAtIndex + 1);

                    var colonIndex = userPass.IndexOf(':');
                    var user = colonIndex > 0 ? userPass.Substring(0, colonIndex) : userPass;
                    var pass = colonIndex > 0 ? userPass.Substring(colonIndex + 1) : "";

                    var hostPortSplit = hostPort.Split(':');
                    var host = hostPortSplit[0];
                    var port = hostPortSplit.Length > 1 ? hostPortSplit[1] : "5432";

                    user = Uri.UnescapeDataString(user);
                    pass = Uri.UnescapeDataString(pass);

                    return $"Host={host};Port={port};Database={dbName};Username={user};Password={pass};Ssl Mode=VerifyFull;";
                }
            }
            catch
            {
                // Fallback to raw string if custom parsing fails
            }
        }

        return connectionString;
    }
}
