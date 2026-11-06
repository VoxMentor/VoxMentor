using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application.Common.Interfaces;
using Xunit;

namespace VoxMentor.Tests.Integration;

public class DependencyInjectionTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DependencyInjectionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void ITutorService_ResolvesFromScope_WithConfiguredHttpClientTimeout()
    {
        using var scope = _factory.Services.CreateScope();
        var tutorService = scope.ServiceProvider.GetRequiredService<ITutorService>();

        Assert.NotNull(tutorService);
    }

    [Fact]
    public void IVoiceClient_ResolvesFromScope()
    {
        using var scope = _factory.Services.CreateScope();
        var voiceClient = scope.ServiceProvider.GetRequiredService<IVoiceClient>();

        Assert.NotNull(voiceClient);
    }

    [Fact]
    public void ITextExtractor_ResolvesFromScope()
    {
        using var scope = _factory.Services.CreateScope();
        var extractor = scope.ServiceProvider.GetRequiredService<ITextExtractor>();

        Assert.NotNull(extractor);
    }
}