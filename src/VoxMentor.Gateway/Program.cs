using Serilog;
using Hangfire;
using Hangfire.PostgreSql;
using VoxMentor.Gateway.Jobs;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));

// YARP reverse proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Hangfire (Supabase Postgres)
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options =>
        options.UseNpgsqlConnection(
            builder.Configuration.GetConnectionString("Hangfire"))));
builder.Services.AddHangfireServer();

var app = builder.Build();

app.UseSerilogRequestLogging();

// YARP routes
app.MapReverseProxy();

// Hangfire dashboard (dev only)
if (app.Environment.IsDevelopment())
{
    app.MapHangfireDashboard("/hangfire");
}

// Register nightly job
RecurringJob.AddOrUpdate<NightlyJob>(
    "nightly-cleanup",
    job => job.ExecuteAsync(CancellationToken.None),
    Cron.Daily);

app.Run();
