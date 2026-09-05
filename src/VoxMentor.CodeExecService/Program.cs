using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using VoxMentor.CodeExecService.Clients;
using VoxMentor.CodeExecService.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Judge0 client + execution service
builder.Services.AddHttpClient<Judge0Client>();
builder.Services.AddScoped<CodeExecutionService>();

// JWT Authentication (shared config via environment variables)
var jwtSecret = builder.Configuration["JwtSettings:Secret"]
    ?? Environment.GetEnvironmentVariable("JwtSettings__Secret");
var jwtIssuer = builder.Configuration["JwtSettings:Issuer"]
    ?? Environment.GetEnvironmentVariable("JwtSettings__Issuer");
var jwtAudience = builder.Configuration["JwtSettings:Audience"]
    ?? Environment.GetEnvironmentVariable("JwtSettings__Audience");

if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    throw new InvalidOperationException(
        "Missing or invalid JWT configuration: 'JwtSettings:Secret' must be a non-empty string of at least 32 characters. " +
        "Set it via configuration or the 'JwtSettings__Secret' environment variable.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Health check (always available)
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
