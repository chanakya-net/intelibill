using System.Net;
using System.Net.Http.Json;
using Intelibill.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using Xunit;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class RateLimitIntegrationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public async Task InitializeAsync() => await _factory.InitializeAsync();
    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Login_ShouldBeRateLimited_AfterExceedingLimit()
    {
        // Arrange
        using var scopeFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();
            });
        });

        var client = scopeFactory.CreateClient();
        var loginRequest = new LoginWithEmailRequest("test@example.com", "Password123!");

        // Act & Assert
        // Limit is 10 per 1 min
        for (int i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync("api/auth/login/email", loginRequest);
            Assert.NotEqual((HttpStatusCode)429, response.StatusCode);
        }

        // 11th request should be rate limited
        var rateLimitedResponse = await client.PostAsJsonAsync("api/auth/login/email", loginRequest);
        Assert.Equal((HttpStatusCode)429, rateLimitedResponse.StatusCode);
    }

    [Fact]
    public async Task Login_ShouldTriggerBackoff_WhenLimitExceeded()
    {
        // Arrange
        using var scopeFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();
            });
        });

        var client = scopeFactory.CreateClient();
        var loginRequest = new LoginWithEmailRequest("test@example.com", "Password123!");

        // 1. Hit the limit
        for (int i = 0; i < 11; i++)
        {
            await client.PostAsJsonAsync("api/auth/login/email", loginRequest);
        }

        // 2. Verify we are blocked (even if we wait a bit, but less than 3 mins)
        var blockedResponse = await client.PostAsJsonAsync("api/auth/login/email", loginRequest);
        Assert.Equal((HttpStatusCode)429, blockedResponse.StatusCode);
        
        var content = await blockedResponse.Content.ReadAsStringAsync();
        Assert.Contains("Rate limit reached or backoff in effect", content);
    }
}
