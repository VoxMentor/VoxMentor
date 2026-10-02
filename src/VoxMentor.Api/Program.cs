using System.Diagnostics;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Api.Authorization;
using VoxMentor.Api.Hubs;
using VoxMentor.Api.Middleware;
using VoxMentor.Api.Services;
using VoxMentor.Application;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Infrastructure;
using VoxMentor.Infrastructure.Jobs;
using VoxMentor.Infrastructure.Persistence.Seeders;
using VoxMentor.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Layer Dependencies
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// Hangfire — textbook ingestion (#70) and the nightly jobs (#57).
// ConnectionStrings:Hangfire wins, falls back to DefaultConnection; explicit
// empty string (integration tests) disables it.
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
// #112: model-binding failures return the ApiResponse envelope instead of the
// default ValidationProblemDetails, so every 4xx the client sees parses the same way.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(kv => kv.Value != null && kv.Value.Errors.Count > 0)
            .ToDictionary(
                kv => kv.Key,
                kv => kv.Value!.Errors
                    .Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? e.Exception?.Message ?? "" : e.ErrorMessage)
                    .Where(s => s.Length > 0)
                    .ToArray());
        var body = ApiResponse<object>.FailureResult("Validation failed.", errors.Count > 0 ? errors : null);
        body.TraceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(body);
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.CustomSchemaIds(x => x.FullName));

builder.Services.AddSignalR();

// Overrides the NullMasteryEventPublisher from Infrastructure so BKT updates
// reach /hubs/mastery (#73).
builder.Services.AddScoped<IMasteryEventPublisher, SignalRMasteryEventPublisher>();

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

// Named RBAC policies (#82). JwtTokenGenerator already emits one claim per role.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.ManageContent, policy => policy.RequireRole("ContentAdmin", "SuperAdmin"));
    options.AddPolicy(Policies.ManagePlatform, policy => policy.RequireRole("PlatformAdmin", "SuperAdmin"));
    options.AddPolicy(Policies.ManageRoles, policy => policy.RequireRole("SuperAdmin"));
});

var app = builder.Build();

// Without this the two nightly jobs silently never exist: the AddOrUpdate block
// further down is skipped and nothing in the log says so (#57).
if (!hangfireEnabled)
{
    app.Logger.LogWarning(
        "Hangfire disabled (neither ConnectionStrings:Hangfire nor DefaultConnection is set): "
        + "textbook uploads will return 500 and the BKT tuning + spaced-repetition jobs will not run.");
}

// Seed Roles and Migrate DB
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var dbContext = services.GetRequiredService<VoxMentor.Infrastructure.Persistence.ApplicationDbContext>();
    if (dbContext.Database.IsRelational())
    {
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(dbContext.Database);
        // CodeRabbit #96: production gate — refuse to boot on pgvector < 0.8.
        // Older versions accept hnsw.iterative_scan as a placeholder and silently
        // ignore it, so retrieval would degrade without any error anywhere.
        if (dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            var pgVer = await dbContext.Database
                .SqlQuery<string>(VoxMentor.Infrastructure.Persistence.PgvectorVersion.Sql)
                .FirstOrDefaultAsync();
            if (!VoxMentor.Infrastructure.Persistence.PgvectorVersion.IsSupported(pgVer))
            {
                throw new InvalidOperationException(
                    $"pgvector >= 0.8.0 required for hnsw.iterative_scan, found: {pgVer ?? "not installed"}");
            }
        }
    }
    await RoleSeeder.SeedRolesAsync(services);
}

// Configure HTTP request pipeline
app.UseMiddleware<ExceptionHandlingMiddleware>();

// #112: empty4xx/5xx responses (routing misses, auth challenges, unsupported
// methods...) get an ApiResponse body. Native middleware: only fires when the
// response has no body, so exception/factory responses pass through untouched.
app.UseStatusCodePages(async statusCodeContext =>
{
    var response = statusCodeContext.HttpContext.Response;
    var status = response.StatusCode;
    var message = status switch
    {
        StatusCodes.Status400BadRequest => "Bad request.",
        StatusCodes.Status401Unauthorized => "Authentication is required.",
        StatusCodes.Status403Forbidden => "You do not have permission to perform this action.",
        StatusCodes.Status404NotFound => "The requested resource was not found.",
        StatusCodes.Status405MethodNotAllowed => "The HTTP method is not allowed for this resource.",
        _ when status >= 500 => "An error occurred while processing your request.",
        _ => "The request could not be processed."
    };
    var body = ApiResponse<object>.FailureResult(message);
    body.TraceId = Activity.Current?.Id ?? response.HttpContext.TraceIdentifier;
    await response.WriteAsJsonAsync(body);
});

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
    // Role-gated via the named policy (#82): previously open to any request
    // reaching the dev server. The helper clears Hangfire's default
    // local-requests-only filter and applies ManagePlatform instead.
    app.MapHangfireDashboardWithAuthorizationPolicy(
        authorizationPolicyName: Policies.ManagePlatform,
        pattern: "/hangfire");
}

// Nightly jobs (#57): UTC crons — BKT EM tuning 02:00, spaced-repetition
// decay 03:00. AddOrUpdate is idempotent, so re-registration is a no-op.
if (hangfireEnabled)
{
    using var scope = app.Services.CreateScope();
    var manager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    manager.AddOrUpdate<BktParameterTuningJob>(
        "bkt-parameter-tuning",
        job => job.ExecuteAsync(CancellationToken.None),
        "0 2 * * *");
    manager.AddOrUpdate<SpacedRepetitionDecayJob>(
        "spaced-repetition-decay",
        job => job.ExecuteAsync(CancellationToken.None),
        "0 3 * * *");
    app.Logger.LogInformation(
        "Hangfire: registered nightly jobs bkt-parameter-tuning (0 2 * * *) and "
        + "spaced-repetition-decay (0 3 * * *), both UTC.");
}

app.MapHub<TutorHub>("/hubs/tutor");
app.MapHub<MasteryHub>("/hubs/mastery");
app.MapHub<InterviewHub>("/hubs/interview");

app.Run();

public partial class Program { }
