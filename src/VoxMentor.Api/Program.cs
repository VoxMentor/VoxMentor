using Microsoft.EntityFrameworkCore;
using VoxMentor.Api.Middleware;
using VoxMentor.Application;
using VoxMentor.Infrastructure;
using VoxMentor.Infrastructure.Persistence.Seeders;

var builder = WebApplication.CreateBuilder(args);

// Add Layer Dependencies
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
