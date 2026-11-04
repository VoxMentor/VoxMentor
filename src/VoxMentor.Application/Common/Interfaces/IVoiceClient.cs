namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Voice Service (faster-whisper STT + Piper TTS) abstraction: transcribe
/// candidate audio, synthesize interviewer replies to <c>audio/wav</c>, and
/// probe backend health. Implementations swallow nothing — callers decide how
/// to degrade (the mock-interview dialogue degrades to text-only).
/// </summary>
public interface IVoiceClient
{
    /// <summary>Transcribes uploaded audio via <c>POST /stt</c> (multipart field "audio").</summary>
    Task<SttResult> TranscribeAsync(Stream audio, string contentType, CancellationToken ct = default);

    /// <summary>Synthesizes text to WAV audio via <c>POST /tts</c> (JSON { text, voice }).</summary>
    Task<byte[]> SynthesizeAsync(string text, string? voice = null, CancellationToken ct = default);

    /// <summary>Probes <c>GET /health</c>; false when the backend is unreachable or unhealthy.</summary>
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}

/// <summary>STT result from <see cref="IVoiceClient.TranscribeAsync"/>.</summary>
/// <param name="Text">Transcribed transcript.</param>
/// <param name="Language">Detected language code (e.g. <c>en</c>).</param>
/// <param name="DurationMs">Audio duration reported by the backend.</param>
public record SttResult(string Text, string Language, int DurationMs);
