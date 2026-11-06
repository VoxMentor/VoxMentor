using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Infrastructure.Services;

/// <summary>
/// Typed client for the Voice Service (Python FastAPI, port 8001): faster-whisper
/// STT at <c>POST /stt</c>, Piper TTS at <c>POST /tts</c>, health at <c>GET /health</c>.
/// Non-success responses (including the backend's 503s) surface as exceptions so
/// the dialogue layer can degrade to text-only interviews instead of hanging.
/// </summary>
public class VoiceServiceClient : IVoiceClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly ILogger<VoiceServiceClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Initialises a new instance of the <see cref="VoiceServiceClient"/> class.</summary>
    /// <param name="http">The HTTP client used to call the Voice Service.</param>
    /// <param name="config">Application configuration providing the service base URL.</param>
    /// <param name="logger">Logger for voice-service diagnostics.</param>
    public VoiceServiceClient(
        HttpClient http,
        IConfiguration config,
        ILogger<VoiceServiceClient> logger)
    {
        _http = http;
        _baseUrl = (config["VoiceService:BaseUrl"] ?? "http://localhost:8001").TrimEnd('/');
        _logger = logger;
    }

    /// <summary>Transcribes uploaded audio via <c>POST /stt</c>.</summary>
    public async Task<SttResult> TranscribeAsync(
        Stream audio, string contentType, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(audio);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(streamContent, "audio", "upload.wav");

        using var response = await _http.PostAsync($"{_baseUrl}/stt", form, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SttResponse>(JsonOptions, timeoutCts.Token)
            ?? throw new InvalidOperationException("The voice service returned an empty STT response.");

        return new SttResult(body.Text, body.Language, body.DurationMs);
    }

    /// <summary>Synthesizes text to <c>audio/wav</c> via <c>POST /tts</c>.</summary>
    public async Task<byte[]> SynthesizeAsync(
        string text, string? voice = null, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

        var payload = new { text, voice };
        using var response = await _http.PostAsJsonAsync($"{_baseUrl}/tts", payload, JsonOptions, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(timeoutCts.Token);
    }

    /// <summary>Probes <c>GET /health</c>; false when the backend is unreachable or unhealthy.</summary>
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync($"{_baseUrl}/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Voice service health check failed at {BaseUrl}", _baseUrl);
            return false;
        }
    }

    /// <summary>JSON response from the Voice Service STT endpoint.</summary>
    private sealed class SttResponse
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("durationMs")]
        public int DurationMs { get; set; }
    }
}
