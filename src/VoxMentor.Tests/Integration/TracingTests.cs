using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace VoxMentor.Tests.Integration;

/// <summary>
/// A factory that keeps the exported spans in memory so the tracing tests can
/// assert on them (#154). The exporter is bolted onto the provider the API
/// registers, so the wiring under test is the real one.
/// </summary>
public class TracingWebApplicationFactory : CustomWebApplicationFactory
{
    /// <summary>The spans the API exported since the host started.</summary>
    public List<Activity> Exported { get; } = new();

    /// <summary>Guards <see cref="Exported"/> - every reader and writer takes it.</summary>
    public object ExportedLock { get; } = new();

    /// <summary>The in-memory exporter the API's provider writes to.</summary>
    public BaseExporter<Activity> Exporter { get; }

    public TracingWebApplicationFactory()
    {
        // Test-only OTEL settings (#154): a developer's exported
        // OTEL_SERVICE_NAME would break the fixed service-name assertion, and
        // an exported endpoint/exporter would make these tests depend on a
        // running collector. Clearing them before the host starts keeps the
        // zero-config path under test regardless of the shell (CodeRabbit #176).
        Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", null);
        Environment.SetEnvironmentVariable("OTEL_TRACES_EXPORTER", null);
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", null);

        Exporter = new LockingExporter(Exported, ExportedLock);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            // SimpleActivityExportProcessor exports on span end, so a test does
            // not have to wait for the batch processor's timer.
            services.ConfigureOpenTelemetryTracerProvider(tracing =>
                tracing.AddProcessor(new SimpleActivityExportProcessor(Exporter)));
        });
    }

    /// <summary>
    /// Appends under <see cref="ExportedLock"/>, the same lock the tests read
    /// and clear with - the provider exports on a request thread, so an
    /// unsynchronized collection could be mutated mid-enumeration.
    /// </summary>
    private sealed class LockingExporter : BaseExporter<Activity>
    {
        private readonly List<Activity> _target;
        private readonly object _gate;

        public LockingExporter(List<Activity> target, object gate)
        {
            _target = target;
            _gate = gate;
        }

        /// <summary>Appends one batch under the gate the readers also take.</summary>
        public override ExportResult Export(in Batch<Activity> batch)
        {
            lock (_gate)
            {
                foreach (var span in batch) _target.Add(span);
            }
            return ExportResult.Success;
        }
    }
}

/// <summary>
/// OpenTelemetry distributed tracing (#154): the API records an ASP.NET Core
/// span per request and keeps serving requests with no exporter configured -
/// the zero-config local dev case, where no collector is running.
/// </summary>
public class TracingTests : IClassFixture<TracingWebApplicationFactory>
{
    /// <summary>A path no other test touches, so the span belongs to this test.</summary>
    private const string ProbePath = "/api/v1/tracing-probe";

    private readonly TracingWebApplicationFactory _factory;

    public TracingTests(TracingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// The API must answer a request with no collector running - the exporter
    /// is only configured when OTEL_TRACES_EXPORTER / OTEL_EXPORTER_OTLP_ENDPOINT
    /// say so, and the test environment sets neither.
    /// </summary>
    [Fact]
    public async Task Api_serves_requests_without_a_collector()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(ProbePath);

        // No route matches the probe path, so the API answers 404 - but it
        // answers, which is the point: tracing must not stop the API booting.
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Every request must produce an ASP.NET Core span carrying a trace id,
    /// tagged with the service name the API reports to the collector.
    /// </summary>
    [Fact]
    public async Task Requests_are_traced()
    {
        var client = _factory.CreateClient();

        // The two tests share the probe path and the in-memory exporter, so
        // clear previously exported spans before asserting on this test's own
        // span — otherwise WaitForSpan could match a span left by another
        // test.
        lock (_factory.ExportedLock)
        {
            _factory.Exported.Clear();
        }

        await client.GetAsync(ProbePath);

        var span = WaitForSpan("tracing-probe", TimeSpan.FromSeconds(5));

        Assert.True(span != null, "exported spans: " + Describe());
        Assert.Equal("Microsoft.AspNetCore", span!.Source.Name);
        Assert.NotEqual(new string('0', 32), span.TraceId.ToHexString());
        Assert.Equal("voxmentor-api", ServiceName());
    }

    /// <summary>Waits for the exported span that covers the given path fragment.</summary>
    private Activity? WaitForSpan(string fragment, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Activity? span;
            lock (_factory.ExportedLock)
            {
                span = _factory.Exported.FirstOrDefault(s => Covers(s, fragment));
            }
            if (span != null) return span;
            Thread.Sleep(25);
        }
        return null;
    }

    /// <summary>
    /// A span covers the fragment when its display name or one of its URL tags
    /// mentions it. An unmatched route leaves the display name as the bare
    /// HTTP method, so the URL tag is the reliable marker.
    /// </summary>
    private static bool Covers(Activity span, string fragment)
    {
        if (span.DisplayName.Contains(fragment, StringComparison.OrdinalIgnoreCase)) return true;
        return new[] { "url.path", "url.full", "http.target" }
            .Select(tag => span.GetTagItem(tag) as string)
            .Any(value => value != null && value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Summarises the exported spans for a failure message.</summary>
    private string Describe()
    {
        lock (_factory.ExportedLock)
        {
            return string.Join(" | ", _factory.Exported.Select(s =>
                $"{s.Source.Name}:{s.DisplayName} path={s.GetTagItem("url.path")}"));
        }
    }

    /// <summary>Reads service.name off the resource the provider reports.</summary>
    private string? ServiceName()
    {
        var provider = _factory.Services.GetRequiredService<TracerProvider>();
        return provider.GetResource().Attributes
            .FirstOrDefault(a => a.Key == "service.name")
            .Value as string;
    }
}
