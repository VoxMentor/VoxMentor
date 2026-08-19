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
        .AddDefaultTokenProviders();

        var jwtSettings = new JwtSettings();
        configuration.Bind(JwtSettings.SectionName, jwtSettings);
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IHealthService, HealthService>();

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
            return "Host=localhost;Database=voxmentordb;Username=postgres;Password=postgres;";
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

                    return $"Host={host};Port={port};Database={dbName};Username={user};Password={pass};Ssl Mode=Require;Trust Server Certificate=true;";
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
