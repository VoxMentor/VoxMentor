using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace VoxMentor.Tests.Integration;

/// <summary>Every error response — thrown, bound, or bare status — parses as the ApiResponse envelope with a traceId (#112).</summary>
public class ErrorResponseApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ErrorResponseApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UnknownRoute_Returns404_AsApiResponse_WithTraceId()
    {
        var response = await _client.GetAsync("/api/v1/this-route-does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertApiResponse(response);
    }

    [Fact]
    public async Task AnonymousRequest_Returns401_AsApiResponse_WithTraceId()
    {
        var response = await _client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertApiResponse(response);
    }

    [Fact]
    public async Task ModelBindingFailure_Returns400_AsApiResponse_WithErrorsAndTraceId()
    {
        // Number where the DTO expects a string: binding fails before the action runs,
        // so this exercises InvalidModelStateResponseFactory rather than the middleware.
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = 123, password = 456 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await AssertApiResponse(response);

        Assert.True(body.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.EnumerateObject().Any());
    }

    private static async Task<JsonDocument> AssertApiResponse(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        return doc;
    }
}
