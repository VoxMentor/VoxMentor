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

import io
import logging
import os
import re
import subprocess
import sys
import threading
import wave
from typing import Annotated

from fastapi import FastAPI, File, HTTPException, UploadFile
from fastapi.responses import Response
from pydantic import BaseModel, Field

log = logging.getLogger("voice-service")
logging.basicConfig(level=logging.INFO)

MODELS_DIR = os.environ.get("VOICE_MODELS_DIR", "/models")
WHISPER_MODEL = os.environ.get("WHISPER_MODEL", "base")
WHISPER_DEVICE = os.environ.get("WHISPER_DEVICE", "cpu")
WHISPER_COMPUTE_TYPE = os.environ.get("WHISPER_COMPUTE_TYPE", "int8")
PIPER_VOICE = os.environ.get("PIPER_VOICE", "en_US-amy-medium")
MAX_UPLOAD_BYTES = 25 * 1024 * 1024

# The API contract (docs/API.md) uses logical voices like "en_IN-female";
# Piper only ships en_US/en_GB voices, so map them onto the closest real one.
VOICE_ALIASES = {
    "en_IN-female": "en_US-amy-medium",
    "en-IN-female": "en_US-amy-medium",
    "en_US-female": "en_US-amy-medium",
    "en-US-female": "en_US-amy-medium",
}
_PIPER_VOICE_RE = re.compile(r"^[a-z]{2}_[A-Z]{2}-[A-Za-z0-9_'-]+$")

app = FastAPI(title="VoxMentor Voice Service")

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


@app.post("/stt")
def stt(audio: Annotated[UploadFile, File()]):
    content_type = (audio.content_type or "").lower()
    if not (content_type.startswith("audio/") or content_type == "application/octet-stream"):
        raise HTTPException(415, f"unsupported content type {content_type!r}; send audio/*")

    data = audio.file.read(MAX_UPLOAD_BYTES + 1)
    if not data:
        raise HTTPException(400, "empty audio upload")
    if len(data) > MAX_UPLOAD_BYTES:
        raise HTTPException(413, f"audio exceeds the {MAX_UPLOAD_BYTES // (1024 * 1024)} MB limit")

    model = _get_whisper()
    try:
        segments, info = model.transcribe(io.BytesIO(data))
        text = " ".join(segment.text.strip() for segment in segments).strip()
    except Exception as exc:
        # undecodable payloads surface while the segment generator is consumed
        raise HTTPException(400, f"could not transcribe audio: {exc}") from exc

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
            )
        except (FileNotFoundError, subprocess.CalledProcessError) as exc:
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


@app.post("/tts")
def tts(request: TtsRequest):
    name = _resolve_voice(request.voice)
    try:
        voice = _load_voice(name)
    except HTTPException:
        if name == PIPER_VOICE:
            raise
        log.warning("voice %r unavailable; falling back to %s", name, PIPER_VOICE)
        voice = _load_voice(PIPER_VOICE)

    buffer = io.BytesIO()
    try:
        with wave.open(buffer, "wb") as wav_file:
            voice.synthesize_wav(request.text, wav_file)
    except Exception as exc:
        raise HTTPException(500, f"speech synthesis failed: {exc}") from exc
    return Response(content=buffer.getvalue(), media_type="audio/wav")


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("main:app", host="0.0.0.0", port=int(os.environ.get("VOICE_PORT", "8001")))
