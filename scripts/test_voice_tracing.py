#!/usr/bin/env python3
"""Offline checks for the voice service's OpenTelemetry wiring (issue #154).

No collector and no network: main.py installs a tracer provider, and the check
bolts an in-memory exporter onto it to prove that a request produces a span
and that the provider reports the voice service's own service.name.

Run: python -m unittest scripts.test_voice_tracing
"""
import asyncio
import importlib.util
import os
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
VOICE_SERVICE = ROOT / "voice-service"

# No collector in the test environment: the provider must record the spans and
# drop them, not try to reach one. Capture the originals before clearing so the
# process environment is restored once the module's tests are done (the OTEL
# SDK reads these at provider-build time, so clearing is process-wide).
_ORIG_OTEL_ENV = {
    "OTEL_EXPORTER_OTLP_ENDPOINT": os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT"),
    "OTEL_SERVICE_NAME": os.environ.get("OTEL_SERVICE_NAME"),
    "OTEL_SDK_DISABLED": os.environ.get("OTEL_SDK_DISABLED"),
    "OTEL_TRACES_SAMPLER": os.environ.get("OTEL_TRACES_SAMPLER"),
    "OTEL_TRACES_SAMPLER_ARG": os.environ.get("OTEL_TRACES_SAMPLER_ARG"),
}
# Clear all of them for the test run; originals are restored in tearDownModule.
for _k in _ORIG_OTEL_ENV:
    os.environ.pop(_k, None)

sys.path.insert(0, str(VOICE_SERVICE))
# The service module keeps its plain name, so load it by path. Importing it
# needs fastapi and the OpenTelemetry SDK; when those optional deps are not
# installed the tests skip instead of failing an unrelated suite run.
_IMPORT_ERROR: str | None = None
try:
    _spec = importlib.util.spec_from_file_location("voice_main", VOICE_SERVICE / "main.py")
    main = importlib.util.module_from_spec(_spec)
    sys.modules["voice_main"] = main
    _spec.loader.exec_module(main)
except ImportError as exc:  # pragma: no cover - depends on the local env
    _IMPORT_ERROR = f"{type(exc).__name__}: {exc}"

_READY = _IMPORT_ERROR is None and bool(getattr(main, "_OTEL_AVAILABLE", False))


@unittest.skipUnless(
    _READY,
    f"voice tracing deps unavailable ({_IMPORT_ERROR or 'opentelemetry not installed'})",
)
class VoiceTracingTests(unittest.TestCase):
    def test_request_produces_a_span(self):
        """A health request must end one ASP.NET-style server span with a real trace id."""
        from opentelemetry import trace
        from opentelemetry.sdk.trace.export import SimpleSpanProcessor
        from opentelemetry.sdk.trace.export.in_memory_span_exporter import (
            InMemorySpanExporter,
        )

        provider = trace.get_tracer_provider()
        exporter = InMemorySpanExporter()
        provider.add_span_processor(SimpleSpanProcessor(exporter))

        async def run():
            from httpx import ASGITransport, AsyncClient

            async with AsyncClient(
                transport=ASGITransport(app=main.app),
                base_url="http://testserver",
            ) as client:
                response = await client.get("/health")
            return response

        response = asyncio.run(run())

        self.assertEqual(200, response.status_code)
        spans = exporter.get_finished_spans()
        self.assertTrue(spans, "the request produced no span")
        span = spans[0]
        self.assertIn("GET", span.name)
        self.assertNotEqual(0, span.get_span_context().trace_id)

    def test_provider_reports_the_voice_service_name(self):
        """The provider must advertise the voice service's own service.name."""
        from opentelemetry import trace

        provider = trace.get_tracer_provider()
        service_name = provider.resource.attributes.get("service.name")
        self.assertEqual("voxmentor-voice-service", service_name)


if __name__ == "__main__":
    unittest.main()


def tearDownModule():
    # Restore the OTEL environment this module cleared at import time, so a
    # later module in the same process (or a manual re-run) is not affected.
    for _k, _v in _ORIG_OTEL_ENV.items():
        if _v is None:
            os.environ.pop(_k, None)
        else:
            os.environ[_k] = _v
