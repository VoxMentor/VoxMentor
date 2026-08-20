using System.Net;
using System.Net.Http.Json;
using VoxMentor.Application.Common.Models;
using Xunit;

namespace VoxMentor.Tests.Integration;

public class HealthApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_Returns200OK_WithStructuredPostgresCheckStatus()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<HealthCheckResultDto>();
        Assert.NotNull(result);
        Assert.True(result.Status == "Healthy" || result.Status == "Unhealthy");
        Assert.NotNull(result.Checks);
        Assert.True(result.Checks.ContainsKey("postgres"));
    }
}
