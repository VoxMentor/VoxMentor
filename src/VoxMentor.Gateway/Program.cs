using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog. ReadFrom.Configuration is the current spelling; the legacy
// ReadFromConfiguration was only compiling because the Hangfire packages
// dragged Serilog.Settings.Configuration in transitively (#57).
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(builder.Configuration));

// YARP reverse proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// No Hangfire server here, deliberately (#57). The Gateway is a YARP proxy: it
// registers no jobs and does not reference VoxMentor.Infrastructure, so it
// cannot construct any (BktParameterTuningJob needs ApplicationDbContext).
// Hangfire lets several servers share one storage and coordinates them with
// distributed locks, so a worker started here would dequeue the nightly jobs,
// fail to activate them, and burn the job's single retry - starving the jobs
// that VoxMentor.Api does run. Job activation resolves through the ASP.NET Core
// DI container, which is where that failure comes from.
// The API owns Hangfire: the gated /hangfire dashboard (#82) and the nightly
// jobs (#57) both live there.

var app = builder.Build();

app.UseSerilogRequestLogging();

// YARP routes
app.MapReverseProxy();

app.Run();
