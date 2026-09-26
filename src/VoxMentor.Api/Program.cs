using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Api.Hubs;
using VoxMentor.Api.Middleware;
using VoxMentor.Application;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Infrastructure;
using VoxMentor.Infrastructure.Persistence.Seeders;
using VoxMentor.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Layer Dependencies
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// Hangfire — textbook ingestion (#70). ConnectionStrings:Hangfire wins, falls back
// to DefaultConnection; explicit empty string (integration tests) disables it.
var hangfireCs = builder.Configuration.GetConnectionString("Hangfire")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");
var hangfireEnabled = !string.IsNullOrWhiteSpace(hangfireCs);
if (hangfireEnabled)
{
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(hangfireCs!)));
    builder.Services.AddHangfireServer();
    builder.Services.AddScoped<ITextbookIngestionQueue, TextbookIngestionQueue>();
}
else
{
    // Fail-fast queue so design-time DI still resolves it; uploads 500 until
    // a Hangfire connection string is configured.
    builder.Services.AddScoped<ITextbookIngestionQueue, NullTextbookIngestionQueue>();
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.CustomSchemaIds(x => x.FullName));

builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "https://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Seed Roles and Migrate DB
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var dbContext = services.GetRequiredService<VoxMentor.Infrastructure.Persistence.ApplicationDbContext>();
    if (dbContext.Database.IsRelational())
    {
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(dbContext.Database);
    }
    await RoleSeeder.SeedRolesAsync(services);
}

// Configure HTTP request pipeline
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsDevelopment() && hangfireEnabled)
{
    app.MapHangfireDashboard("/hangfire");
}

app.MapHub<TutorHub>("/hubs/tutor");
app.MapHub<MasteryHub>("/hubs/mastery");
app.MapHub<InterviewHub>("/hubs/interview");

app.Run();

public partial class Program { }
