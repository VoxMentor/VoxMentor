using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
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

// OpenTelemetry distributed tracing (#154). The gateway is the entry point
// for the browser, so its server span is the root of a trace: YARP forwards
// the incoming `traceparent` header and the HttpClient instrumentation
// re-injects the proxy span's context, so the API and the voice service
// continue the same trace. Zero-config: with no exporter configured the
// provider records spans and drops them.
//   OTEL_TRACES_EXPORTER=console  -> print the spans to stdout (local dev)
//   OTEL_EXPORTER_OTLP_ENDPOINT   -> export over OTLP to the collector
//   OTEL_SERVICE_NAME             -> service.name carried on every span
var otelExporter = (builder.Configuration["OTEL_TRACES_EXPORTER"] ?? string.Empty).Trim().ToLowerInvariant();
var otelEndpoint = (builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? string.Empty).Trim();
// Trim before falling back: an OTEL_SERVICE_NAME of "" or "   " would reach
// AddService as an empty string, which OpenTelemetry rejects at startup.
var otelServiceName = (builder.Configuration["OTEL_SERVICE_NAME"] ?? string.Empty).Trim();
if (otelServiceName.Length == 0) otelServiceName = "voxmentor-gateway";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(otelServiceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            // YARP's own proxy spans; the source name is fixed upstream.
            .AddSource("Yarp.ReverseProxy");

        if (otelExporter == "console")
        {
            tracing.AddConsoleExporter();
        }
        else if (otelExporter != "none" && otelEndpoint.Length > 0)
        {
            tracing.AddOtlpExporter();
        }
    });

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
