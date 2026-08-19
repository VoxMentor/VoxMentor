using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Features.Auth.Register;
using VoxMentor.Domain.Entities;
using VoxMentor.Infrastructure.Persistence;
using Xunit;

namespace VoxMentor.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));

            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            var appDbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IApplicationDbContext));

            if (appDbContextDescriptor != null)
            {
                services.Remove(appDbContextDescriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
            });

            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        });
    }
}

public class RegisterApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public RegisterApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_ValidStudent_Returns201Created_AndAssignsStudentRole()
    {
        var request = new
        {
            fullName = "Student User",
            email = "student1@example.com",
            password = "Password@123"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<RegisterResponseDto>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("Student User", result.Data.FullName);
        Assert.Equal("student1@example.com", result.Data.Email);
        Assert.Equal("Student", result.Data.Role);

        // Confirm persisted in DB
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == "student1@example.com");
        Assert.NotNull(user);
        Assert.Equal("Student User", user.FullName);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409Conflict()
    {
        var request = new
        {
            fullName = "Duplicate User",
            email = "dupuser@example.com",
            password = "Password@123"
        };

        var firstResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var result = await secondResponse.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("already registered", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_MissingFullName_Returns400BadRequest()
    {
        var request = new
        {
            fullName = "",
            email = "nofullname@example.com",
            password = "Password@123"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidEmail_Returns400BadRequest()
    {
        var request = new
        {
            fullName = "Test User",
            email = "not-an-email",
            password = "Password@123"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WeakPassword_Returns400BadRequest()
    {
        var request = new
        {
            fullName = "Test User",
            email = "weakpass@example.com",
            password = "weak"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_AttemptToPassAdminRole_MustNotCreateAdminRole()
    {
        var request = new
        {
            fullName = "Sneaky Admin Attempt",
            email = "sneakyadmin@example.com",
            password = "Password@123",
            role = "Admin"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<RegisterResponseDto>>();
        Assert.NotNull(result);
        Assert.NotNull(result.Data);
        Assert.Equal("Student", result.Data.Role);
    }
}
