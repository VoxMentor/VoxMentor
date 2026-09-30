using Serilog;
using Hangfire;
using Hangfire.PostgreSql;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));

// YARP reverse proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Hangfire (Supabase Postgres) — only when connection string is configured
var hangfireConnectionString = builder.Configuration.GetConnectionString("Hangfire");
if (!string.IsNullOrEmpty(hangfireConnectionString))
{
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(options =>
            options.UseNpgsqlConnection(hangfireConnectionString)));
    builder.Services.AddHangfireServer();
}

var app = builder.Build();

app.UseSerilogRequestLogging();

// YARP routes
app.MapReverseProxy();

// Hangfire dashboard removed (#82): Gateway has no auth stack, so it cannot be
// role-gated here — use the API's gated /hangfire dashboard instead. Nightly
// jobs live in VoxMentor.Api and register against the same Hangfire storage.

app.Run();
