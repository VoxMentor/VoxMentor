# Distributed tracing (OpenTelemetry)

Issue [#154](https://github.com/VoxMentor/VoxMentor/issues/154).

Every VoxMentor service records OpenTelemetry spans, so one request can be
followed across the gateway, the ASP.NET Core API and the Python voice
service. Spans carry the W3C `traceparent` header from hop to hop, so the
browser call, the API's database queries and the voice service's TTS call all
end up under the same trace id.

## What is instrumented

| Service | Instrumentation | `service.name` |
|---|---|---|
| `VoxMentor.Api` | ASP.NET Core, EF Core, HttpClient | `voxmentor-api` |
| `VoxMentor.Gateway` | ASP.NET Core, HttpClient, YARP proxy spans (`Yarp.ReverseProxy`) | `voxmentor-gateway` |
| `voice-service` | FastAPI | `voxmentor-voice-service` |

## Turning it on

The exporter is chosen from the environment - nothing is enabled by default.

| Variable | Meaning |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Where to send the spans. Set it and the services export over OTLP. |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `http/protobuf` (port 4318) or `grpc` (port 4317). The .NET services default to `grpc`; the voice service only speaks `http/protobuf` — it builds the HTTP exporter and ignores a `grpc` setting, so always point it at 4318. |
| `OTEL_TRACES_EXPORTER` | `console` prints the spans to stdout instead of exporting them. |
| `OTEL_SERVICE_NAME` | Overrides the `service.name` on the spans. |

**Package note.** `OpenTelemetry.Instrumentation.EntityFrameworkCore` is pinned at
the prerelease `1.20.2-beta.1` — it is the only official OTel EF Core tracer,
the build is green, and it is used only for tracing database spans. Pin it
exactly (don't let a restore float it to a later beta) until a stable release
exists.

**Security note.** `OTEL_EXPORTER_OTLP_ENDPOINT` is operator-trusted: anyone
who can write the host environment can point the exporter at an
attacker-controlled collector and receive the recorded spans (which may
include request URLs/query strings). Treat it like a connection string — do
not set it from end-user input, and prefer the same allowlisted collector for
every service in one deployment.

**Zero-config local dev.** With none of these set, the services still record
the spans but drop them: no exporter is registered, nothing is sent anywhere,
and a missing collector can never degrade or crash a service. The API's
integration test `Api_serves_requests_without_a_collector` covers exactly this
case.

A collector that is configured but not running only produces export warnings -
the OTLP exporter retries in the background and never fails a request.

## Running the collector

The compose file ships an OpenTelemetry collector that prints the spans it
receives to its own log:

```bash
docker compose up otel-collector
docker compose logs -f otel-collector
```

It listens on `4317` (gRPC) and `4318` (http/protobuf) on `127.0.0.1`.

Point the host-run services at it:

```bash
# bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318
export OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf

dotnet run --project src/VoxMentor.Api
dotnet run --project src/VoxMentor.Gateway
```

The voice service runs in compose and already points at the collector.
Inside compose it reads the `VOICE_`-prefixed overrides, so a host-shell
`OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318` (set above for the
host-run services) cannot leak into the container, where localhost is the
container itself:

| Variable in `.env` / shell | Effect on the compose voice service |
|---|---|
| unset | exports to `http://otel-collector:4318` (the compose default) |
| `VOICE_OTEL_EXPORTER_OTLP_ENDPOINT=http://other-host:4318` | exports elsewhere |
| `VOICE_OTEL_EXPORTER_OTLP_ENDPOINT=` (explicitly empty) | export off — spans are recorded and dropped |

To see the spans without a collector at all:

```bash
OTEL_TRACES_EXPORTER=console dotnet run --project src/VoxMentor.Api
```

## A sample trace

Captured from the API with `OTEL_TRACES_EXPORTER=console`
(`GET /api/v1/tracing-probe` - the path only exists to give the probe a name):

```
Activity.TraceId:            f19bf40542fa114f111802814cb7b014
Activity.DisplayName:        GET
Activity.Kind:               Server
Activity.StartTime:          2026-11-11T15:06:31.7935090Z
Activity.Duration:           00:00:00.1947503
    service.name: voxmentor-api
```

The trace id is the join key: `f19bf40542fa114f111802814cb7b014` is the id
every other span of that request carries.

### What the end-to-end trace looks like

With the gateway, the API and the voice service all exporting to the collector,
one request produces a tree like this:

```
GET /api/v1/mock/history                       gateway   voxmentor-gateway
└── ProxyRoute                                 gateway   voxmentor-gateway
    └── GET api/v1/mock/history                api       voxmentor-api
        ├── SELECT "m"."Id" ...                api       voxmentor-api   (EF Core)
        └── POST http://voice-service:8001/tts api       voxmentor-api   (HttpClient)
            └── POST /tts                      voice     voxmentor-voice-service
```

The parent/child links are the `traceparent` header: YARP forwards the
incoming header to the API, the API's HttpClient instrumentation re-injects its
own context into the voice service call, and the FastAPI instrumentation picks
it up. One trace id covers the whole path.

## Verifying it

```bash
# .NET: the API records a span per request and still serves requests
#       with no collector running.
dotnet test VoxMentor.slnx --filter "FullyQualifiedName~TracingTests"

# Python: the voice service produces a span for a request.
pip install -r scripts/requirements-voice-tracing.txt
python -m unittest scripts.test_voice_tracing
```

Both run in CI.

## Troubleshooting

- **No spans in the collector.** Check `OTEL_EXPORTER_OTLP_ENDPOINT` is set on
  *the service*, not only in `.env` - the `.env` file is read by compose, the
  host-run API and gateway read your shell.
- **Spans from one service only.** The `traceparent` header only survives if
  the hop forwards it. YARP forwards request headers by default; a custom
  `HttpTransformer` that drops headers would break the link.
- **Export errors in the logs.** The collector is not reachable at the
  configured endpoint. The service keeps serving traffic.
