"""VoxMentor Voice Service — STT (faster-whisper) + TTS (Piper).

Contract: docs/API.md section 9, gateway route `/voice/*` (YARP strips the prefix).

    GET  /health -> {"status": "Healthy"}
    POST /stt    -> multipart field "audio" -> {"text", "language", "durationMs"}
    POST /tts    -> {"text", "voice"} -> audio/wav

ponytail: models load lazily on first request so the container starts (and
passes its healthcheck) without a model download; the first STT/TTS call pays
for the download into VOICE_MODELS_DIR. Swap to a warmup-on-startup if cold
first calls ever matter; they don't yet (Week 5 owns the voice interview flow).
"""

from __future__ import annotations

import glob
import io
import logging
import os
import re
import subprocess
import sys
import threading
import wave

from fastapi import FastAPI, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import Response
from pydantic import BaseModel, Field, ValidationError
from starlette.concurrency import run_in_threadpool
from starlette.datastructures import UploadFile

try:  # av ships with faster-whisper; a bare local env may not have it
    import av.error as _av_error

    _DECODE_ERRORS: tuple[type[Exception], ...] = (_av_error.FFmpegError,)
except ImportError:  # pragma: no cover
    _DECODE_ERRORS = ()

log = logging.getLogger("voice-service")
logging.basicConfig(level=logging.INFO)

MODELS_DIR = os.environ.get("VOICE_MODELS_DIR", "/models")
WHISPER_MODEL = os.environ.get("WHISPER_MODEL", "base")
WHISPER_DEVICE = os.environ.get("WHISPER_DEVICE", "cpu")
WHISPER_COMPUTE_TYPE = os.environ.get("WHISPER_COMPUTE_TYPE", "int8")
PIPER_VOICE = os.environ.get("PIPER_VOICE", "en_US-amy-medium")
MAX_UPLOAD_BYTES = 25 * 1024 * 1024
MAX_UPLOAD_MB = MAX_UPLOAD_BYTES // (1024 * 1024)
# multipart boundary/headers sit on top of the audio part; slack so a max-size
# file is not rejected by the content-length precheck
_MULTIPART_OVERHEAD_BYTES = 8 * 1024
TTS_MAX_BODY_BYTES = 64 * 1024


def _get_int_env(name: str, default: int) -> int:
    raw = (os.environ.get(name) or "").strip()
    if not raw:
        return default
    try:
        value = int(raw)
    except ValueError:
        log.warning("invalid %s %r; using default %d", name, raw, default)
        return default
    if value <= 0:
        log.warning("invalid %s %r; using default %d", name, raw, default)
        return default
    return value


PIPER_DOWNLOAD_TIMEOUT_SECONDS = _get_int_env("PIPER_DOWNLOAD_TIMEOUT_SECONDS", 300)

# The API contract (docs/API.md) uses logical voices like "en_IN-female";
# Piper only ships en_US/en_GB voices, so map them onto the closest real one.
VOICE_ALIASES = {
    "en_IN-female": "en_US-amy-medium",
    "en-IN-female": "en_US-amy-medium",
    "en_US-female": "en_US-amy-medium",
    "en-US-female": "en_US-amy-medium",
}
# Allowlist for the piper.download_voices argv (CWE-78 guard: argv list, no shell).
_PIPER_VOICE_RE = re.compile(r"^[a-z]{2}_[A-Z]{2}-[a-z0-9_-]+$")

class MaxBodySizeMiddleware:
    """Cap the whole request body before Starlette buffers it (CWE-400).

    Starlette's `max_part_size` only caps non-file form fields; file parts spool
    to disk unbounded, and a chunked body has no content-length to precheck, so
    this wraps the ASGI receive channel for every route.
    """

    def __init__(self, app, max_bytes: int):
        self.app = app
        self.max_bytes = max_bytes

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return

        for name, value in scope.get("headers", []):
            if name == b"content-length":
                try:
                    declared = int(value)
                except ValueError:
                    break
                if declared > self.max_bytes:
                    await self._reject(send)
                    return
                break

        received = 0
        rejected = False
        started = False

        async def limited_receive():
            nonlocal received, rejected
            if rejected:
                return {"type": "http.disconnect"}
            message = await receive()
            if message["type"] == "http.request":
                received += len(message.get("body", b""))
                if received > self.max_bytes:
                    rejected = True
                    log.warning("request body exceeded %d bytes; aborting stream", self.max_bytes)
                    return {"type": "http.disconnect"}
            return message

        async def guarded_send(message):
            nonlocal started
            if rejected:
                return  # our 413 is the response; swallow whatever the app tries to send
            if message["type"] == "http.response.start":
                started = True
            await send(message)

        try:
            await self.app(scope, limited_receive, guarded_send)
        except Exception:
            if not rejected:
                raise
        if rejected and not started:
            await self._reject(send)

    async def _reject(self, send):
        body = b'{"detail":"request body too large"}'
        await send(
            {
                "type": "http.response.start",
                "status": 413,
                "headers": [
                    (b"content-type", b"application/json"),
                    (b"content-length", str(len(body)).encode()),
                ],
            }
        )
        await send({"type": "http.response.body", "body": body})


app = FastAPI(title="VoxMentor Voice Service")
app.add_middleware(MaxBodySizeMiddleware, max_bytes=MAX_UPLOAD_BYTES + _MULTIPART_OVERHEAD_BYTES)

_whisper_lock = threading.Lock()
_whisper_model = None


def _get_whisper():
    global _whisper_model
    if _whisper_model is None:
        with _whisper_lock:
            if _whisper_model is None:
                try:
                    from faster_whisper import WhisperModel
                except ImportError as exc:
                    raise HTTPException(503, f"STT unavailable: faster-whisper is not installed ({exc})") from exc
                try:
                    os.makedirs(MODELS_DIR, exist_ok=True)
                    _whisper_model = WhisperModel(
                        WHISPER_MODEL,
                        device=WHISPER_DEVICE,
                        compute_type=WHISPER_COMPUTE_TYPE,
                        download_root=MODELS_DIR,
                    )
                except Exception as exc:
                    raise HTTPException(503, f"STT unavailable: could not load whisper model {WHISPER_MODEL!r} ({exc})") from exc
    return _whisper_model


@app.get("/health")
def health():
    return {"status": "Healthy"}


def _join_segments(segments) -> str:
    return " ".join(segment.text.strip() for segment in segments).strip()


_STT_OPENAPI_EXTRA = {
    "requestBody": {
        "required": True,
        "content": {
            "multipart/form-data": {
                "schema": {
                    "type": "object",
                    "required": ["audio"],
                    "properties": {"audio": {"type": "string", "format": "binary"}},
                }
            }
        },
    }
}


@app.post("/stt", openapi_extra=_STT_OPENAPI_EXTRA)
async def stt(request: Request):
    # CWE-400: reject on the content-length header before Starlette buffers anything;
    # MaxBodySizeMiddleware caps chunked bodies; max_part_size bounds inline fields.
    content_length = request.headers.get("content-length", "")
    if content_length.isdigit() and int(content_length) > MAX_UPLOAD_BYTES + _MULTIPART_OVERHEAD_BYTES:
        raise HTTPException(413, f"audio exceeds the {MAX_UPLOAD_MB} MB limit")
    form = await request.form(max_part_size=MAX_UPLOAD_BYTES)

    audio = form.get("audio")
    if not isinstance(audio, UploadFile):
        raise HTTPException(422, "missing 'audio' form field")

    content_type = (audio.content_type or "").lower()
    if not (content_type.startswith("audio/") or content_type == "application/octet-stream"):
        raise HTTPException(415, f"unsupported content type {content_type!r}; send audio/*")

    data = audio.file.read(MAX_UPLOAD_BYTES + 1)  # defense in depth; the checks above bound the body
    if not data:
        raise HTTPException(400, "empty audio upload")
    if len(data) > MAX_UPLOAD_BYTES:
        raise HTTPException(413, f"audio exceeds the {MAX_UPLOAD_MB} MB limit")

    model = await run_in_threadpool(_get_whisper)
    try:
        # transcribe() decodes the upload eagerly; only segment consumption runs inference
        segments, info = await run_in_threadpool(model.transcribe, io.BytesIO(data))
    except IndexError as exc:
        # PyAV raises IndexError for containers without an audio stream (e.g. video-only files)
        raise HTTPException(400, "could not decode audio: no audio stream found") from exc
    except _DECODE_ERRORS as exc:
        raise HTTPException(400, f"could not decode audio: {exc}") from exc
    except Exception as exc:
        raise HTTPException(503, f"STT backend failed: {exc}") from exc
    try:
        text = await run_in_threadpool(_join_segments, segments)
    except Exception as exc:
        raise HTTPException(503, f"STT backend failed: {exc}") from exc

    return {"text": text, "language": info.language, "durationMs": round(info.duration * 1000)}


class TtsRequest(BaseModel):
    text: str = Field(min_length=1, max_length=2000)
    voice: str | None = None


_voice_lock = threading.Lock()
_voices: dict[str, object] = {}


def _resolve_voice(requested: str | None) -> str:
    name = (requested or "").strip() or PIPER_VOICE
    name = VOICE_ALIASES.get(name, name)
    if not _PIPER_VOICE_RE.match(name):
        log.warning("unknown voice %r; falling back to %s", name, PIPER_VOICE)
        return PIPER_VOICE
    return name


def _remove_partial_voice_files(name: str) -> None:
    for path in glob.glob(os.path.join(MODELS_DIR, f"{name}.onnx*")):
        try:
            os.remove(path)
        except OSError:
            pass


def _ensure_voice_files(name: str) -> str:
    onnx_path = os.path.join(MODELS_DIR, f"{name}.onnx")
    if not os.path.exists(onnx_path):
        os.makedirs(MODELS_DIR, exist_ok=True)
        log.info("downloading Piper voice %s into %s", name, MODELS_DIR)
        try:
            subprocess.run(
                [sys.executable, "-m", "piper.download_voices", name, "--data-dir", MODELS_DIR],
                check=True,
                capture_output=True,
                text=True,
                timeout=PIPER_DOWNLOAD_TIMEOUT_SECONDS,
            )
        except (FileNotFoundError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as exc:
            # a failed/timed-out download must not leave half a voice behind
            _remove_partial_voice_files(name)
            detail = getattr(exc, "stderr", "") or exc
            raise HTTPException(503, f"TTS unavailable: could not download voice {name!r} ({detail})") from exc
    return onnx_path


def _load_voice(name: str):
    with _voice_lock:
        voice = _voices.get(name)
        if voice is None:
            try:
                from piper import PiperVoice
            except ImportError as exc:
                raise HTTPException(503, f"TTS unavailable: piper-tts is not installed ({exc})") from exc
            try:
                voice = PiperVoice.load(_ensure_voice_files(name))
            except HTTPException:
                raise
            except Exception as exc:
                raise HTTPException(503, f"TTS unavailable: could not load voice {name!r} ({exc})") from exc
            _voices[name] = voice
        return voice


def _load_voice_with_fallback(requested: str | None):
    name = _resolve_voice(requested)
    try:
        return _load_voice(name)
    except HTTPException:
        if name == PIPER_VOICE:
            raise
        log.warning("voice %r unavailable; falling back to %s", name, PIPER_VOICE)
        return _load_voice(PIPER_VOICE)


def _synthesize(voice, text: str) -> bytes:
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as wav_file:
        voice.synthesize_wav(text, wav_file)
    return buffer.getvalue()


_TTS_OPENAPI_EXTRA = {
    "requestBody": {
        "required": True,
        "content": {"application/json": {"schema": TtsRequest.model_json_schema()}},
    }
}


@app.post("/tts", openapi_extra=_TTS_OPENAPI_EXTRA)
async def tts(request: Request):
    content_length = request.headers.get("content-length", "")
    if content_length.isdigit() and int(content_length) > TTS_MAX_BODY_BYTES:
        raise HTTPException(413, f"request body exceeds {TTS_MAX_BODY_BYTES // 1024} KB")
    raw = await request.body()  # chunked bodies carry no content-length; check the real size
    if len(raw) > TTS_MAX_BODY_BYTES:
        raise HTTPException(413, f"request body exceeds {TTS_MAX_BODY_BYTES // 1024} KB")
    try:
        payload = TtsRequest.model_validate_json(raw)
    except ValidationError as exc:
        raise RequestValidationError(exc.errors()) from exc

    text = payload.text.strip()
    if not text:
        raise HTTPException(422, "text must not be blank")

    try:
        voice = await run_in_threadpool(_load_voice_with_fallback, payload.voice)
        wav_bytes = await run_in_threadpool(_synthesize, voice, text)
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(503, f"speech synthesis failed: {exc}") from exc
    return Response(content=wav_bytes, media_type="audio/wav")


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("main:app", host="0.0.0.0", port=int(os.environ.get("VOICE_PORT", "8001")))
