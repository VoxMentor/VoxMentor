using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Infrastructure.Services;

namespace VoxMentor.Tests.Unit;

/// <summary>
/// Coverage for <see cref="VoiceServiceClient"/>: 503s and an unreachable
/// backend surface as exceptions so the dialogue layer can degrade to
/// text-only interviews (#152).
/// </summary>
public class VoiceServiceClientTests
{
    private sealed class FakeHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _respond(request, cancellationToken);
    }

    private static VoiceServiceClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var http = new HttpClient(new FakeHandler(respond)) { BaseAddress = new Uri("http://localhost:8001") };
        return new VoiceServiceClient(http, new ConfigurationBuilder().Build(), Microsoft.Extensions.Logging.Abstractions.NullLogger<VoiceServiceClient>.Instance);
    }

    [Fact]
    public async Task SynthesizeAsync_503_ThrowsHttpRequestException()
    {
        var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.SynthesizeAsync("hello"));
        Assert.Contains("503", ex.Message);
    }

    [Fact]
    public async Task SynthesizeAsync_Unreachable_ThrowsHttpRequestException()
    {
        var client = Client((_, _) => throw new HttpRequestException("connect failed"));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SynthesizeAsync("hello"));
    }

    [Fact]
    public async Task SynthesizeAsync_Ok_ReturnsWavBytes()
    {
        var wav = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"
        var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(wav) { Headers = { ContentType = new MediaTypeHeaderValue("audio/wav") } }
        }));
        var result = await client.SynthesizeAsync("hello");
        Assert.Equal(wav, result);
    }

    [Fact]
    public async Task IsHealthyAsync_200_ReturnsTrue()
    {
        var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.True(await client.IsHealthyAsync());
    }

    [Fact]
    public async Task IsHealthyAsync_503_ReturnsFalse()
    {
        var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        Assert.False(await client.IsHealthyAsync());
    }

    [Fact]
    public async Task IsHealthyAsync_Unreachable_ReturnsFalse()
    {
        var client = Client((_, _) => throw new HttpRequestException("connect failed"));
        Assert.False(await client.IsHealthyAsync());
    }

    [Fact]
    public async Task TranscribeAsync_Ok_ReturnsSttResult()
    {
        var json = JsonSerializer.Serialize(new { text = "hi there", language = "en", durationMs = 1200 });
        var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        var result = await client.TranscribeAsync(new MemoryStream(Array.Empty<byte>()), "audio/wav");
        Assert.Equal("hi there", result.Text);
        Assert.Equal("en", result.Language);
        Assert.Equal(1200, result.DurationMs);
    }
}
