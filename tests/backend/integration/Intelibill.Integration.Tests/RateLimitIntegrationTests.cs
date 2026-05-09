using System.Net;
using System.Net.Http.Json;
using Intelibill.Api.Controllers;
using Intelibill.Api.Middleware.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
        using var scopeFactory = CreateRateLimitFactory();
        var client = scopeFactory.CreateClient();
        var loginRequest = new LoginWithEmailRequest("test@example.com", "Password123!");

        for (int i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync("api/auth/login/email", loginRequest);
            Assert.NotEqual((HttpStatusCode)429, response.StatusCode);
        }

        var rateLimitedResponse = await client.PostAsJsonAsync("api/auth/login/email", loginRequest);

        Assert.Equal((HttpStatusCode)429, rateLimitedResponse.StatusCode);
    }

    [Fact]
    public async Task Login_ShouldTriggerBackoff_WhenLimitExceeded()
    {
        using var scopeFactory = CreateRateLimitFactory();
        var client = scopeFactory.CreateClient();
        var loginRequest = new LoginWithEmailRequest("test@example.com", "Password123!");

        for (int i = 0; i < 11; i++)
        {
            await client.PostAsJsonAsync("api/auth/login/email", loginRequest);
        }

        var blockedResponse = await client.PostAsJsonAsync("api/auth/login/email", loginRequest);

        Assert.Equal((HttpStatusCode)429, blockedResponse.StatusCode);

        var content = await blockedResponse.Content.ReadAsStringAsync();
        Assert.Contains("Rate limit reached or backoff in effect", content);
    }

    [Fact]
    public async Task DefaultControllerAction_ShouldUseGlobalRateLimit()
    {
        using var scopeFactory = CreateRateLimitFactory();
        var client = scopeFactory.CreateClient();

        var first = await client.GetAsync("api/rate-limit-test/default");
        var second = await client.GetAsync("api/rate-limit-test/default");
        var third = await client.GetAsync("api/rate-limit-test/default");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((HttpStatusCode)429, third.StatusCode);
    }

    [Fact]
    public async Task DisabledControllerAction_ShouldSkipGlobalRateLimit()
    {
        using var scopeFactory = CreateRateLimitFactory();
        var client = scopeFactory.CreateClient();

        for (int i = 0; i < 5; i++)
        {
            var response = await client.GetAsync("api/rate-limit-test/disabled");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task RateLimitedResponse_ShouldIncludeHeadersAndProblemDetails()
    {
        using var scopeFactory = CreateRateLimitFactory();
        var client = scopeFactory.CreateClient();

        await client.GetAsync("api/rate-limit-test/default");
        await client.GetAsync("api/rate-limit-test/default");
        var response = await client.GetAsync("api/rate-limit-test/default");

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Equal("2", response.Headers.GetValues("X-RateLimit-Limit").Single());
        Assert.Equal("0", response.Headers.GetValues("X-RateLimit-Remaining").Single());
        Assert.Equal("180", response.Headers.GetValues("Retry-After").Single());
        Assert.True(response.Headers.Contains("X-RateLimit-Reset"));

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal("RateLimitExceeded", problemDetails.Title);
        Assert.Equal((int)HttpStatusCode.TooManyRequests, problemDetails.Status);
    }

    [Fact]
    public async Task DifferentQueryStrings_ShouldShareBucket()
    {
        using var scopeFactory = CreateRateLimitFactory();
        var client = scopeFactory.CreateClient();

        var first = await client.GetAsync("api/rate-limit-test/default?search=milk");
        var second = await client.GetAsync("api/rate-limit-test/default?search=bread");
        var third = await client.GetAsync("api/rate-limit-test/default?search=rice");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((HttpStatusCode)429, third.StatusCode);
    }

    [Fact]
    public async Task Backoff_ShouldRemainActiveAfterLimitExceeded()
    {
        using var scopeFactory = CreateRateLimitFactory();
        var client = scopeFactory.CreateClient();

        await client.GetAsync("api/rate-limit-test/default");
        await client.GetAsync("api/rate-limit-test/default");
        var exceeded = await client.GetAsync("api/rate-limit-test/default");
        var blocked = await client.GetAsync("api/rate-limit-test/default");

        Assert.Equal((HttpStatusCode)429, exceeded.StatusCode);
        Assert.Equal((HttpStatusCode)429, blocked.StatusCode);
        Assert.Equal("180", blocked.Headers.GetValues("Retry-After").Single());
    }

    private WebApplicationFactory<Program> CreateRateLimitFactory()
        => _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:Limit"] = "2",
                    ["RateLimiting:PeriodInMinutes"] = "1",
                    ["RateLimiting:BackoffMinutes"] = "3"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.AddControllers()
                    .AddApplicationPart(typeof(RateLimitTestController).Assembly);

                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();
            });
        });

}

[ApiController]
[Route("api/rate-limit-test")]
public sealed class RateLimitTestController : ControllerBase
{
    [HttpGet("default")]
    public IActionResult Default() => Ok(new { Status = "ok" });

    [DisableRateLimit]
    [HttpGet("disabled")]
    public IActionResult Disabled() => Ok(new { Status = "ok" });
}
