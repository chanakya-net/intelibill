using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Api.Controllers;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Options;
using Intelibill.Infrastructure.Services.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The limiter over HTTP, with the shared PostgreSQL store behind it. Callers on different
/// addresses are simulated with <see cref="RateLimitTestPeers.Header"/>.
/// </summary>
[Collection("Integration Tests")]
public sealed class RateLimitIntegrationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string Password = "Correct-Horse-9!";

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

    [Fact]
    public async Task DifferentRecordIds_ShareRouteBudget()
    {
        using var scopeFactory = CreateRateLimitFactory();
        var client = scopeFactory.CreateClient();

        var first = await client.GetAsync("api/rate-limit-test/items/1");
        var second = await client.GetAsync("api/rate-limit-test/items/2");
        var third = await client.GetAsync("api/rate-limit-test/items/3");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((HttpStatusCode)429, third.StatusCode);
    }

    [Fact]
    public async Task RotatingIps_CannotExceedDestinationBudget()
    {
        using var scopeFactory = CreateRateLimitFactory();
        var client = scopeFactory.CreateClient();
        var userId = await SeedVerifiedUserAsync(scopeFactory, "owner@shop.test");

        var statuses = new List<HttpStatusCode>();
        for (var i = 1; i <= 10; i++)
        {
            var response = await PostAsync(client, "api/auth/password-reset/request", new { email = "owner@shop.test" }, peer: $"198.51.100.{i}");
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(7, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
        Assert.Equal(3, await CountResetTokensAsync(scopeFactory, userId));
    }

    [Fact]
    public async Task EmailCaseAndWhitespace_ShareBudget()
    {
        using var scopeFactory = CreateRateLimitFactory(new() { ["RateLimiting:Account:Limit"] = "3" });
        var client = scopeFactory.CreateClient();
        string[] variants = [" User@Example.com ", "user@example.com", "USER@EXAMPLE.COM", "user@Example.com "];

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < variants.Length; i++)
        {
            var response = await PostAsync(client, "api/auth/login/email", new { email = variants[i], password = "wrong-password" }, peer: $"203.0.113.{i + 1}");
            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses.Take(3), s => Assert.NotEqual(HttpStatusCode.TooManyRequests, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[3]);
    }

    [Fact]
    public async Task UnknownAndKnownDestination_HaveSamePublicOutcome()
    {
        using var scopeFactory = CreateRateLimitFactory();
        var client = scopeFactory.CreateClient();
        await SeedVerifiedUserAsync(scopeFactory, "known@shop.test");
        var peer = 0;

        async Task<List<(string Visible, int? RetryAfter)>> ObserveAsync(string email)
        {
            var outcomes = new List<(string Visible, int? RetryAfter)>();
            for (var i = 0; i < 4; i++)
            {
                var response = await PostAsync(client, "api/auth/password-reset/request", new { email }, peer: $"192.0.2.{++peer}");
                outcomes.Add(await PublicOutcomeAsync(response));
            }

            return outcomes;
        }

        var known = await ObserveAsync("known@shop.test");
        var unknown = await ObserveAsync("nobody@shop.test");

        Assert.Equal(known.Select(o => o.Visible), unknown.Select(o => o.Visible));
        Assert.StartsWith("200|", known[0].Visible, StringComparison.Ordinal);
        Assert.StartsWith("429|", known[3].Visible, StringComparison.Ordinal);
        // Retry-After counts down from each destination's own first request, so the two can
        // differ by the second that elapsed between them; what must not differ is its presence.
        Assert.All(known.Zip(unknown), pair =>
        {
            Assert.Equal(pair.First.RetryAfter is null, pair.Second.RetryAfter is null);
            Assert.InRange(Math.Abs((pair.First.RetryAfter ?? 0) - (pair.Second.RetryAfter ?? 0)), 0, 2);
        });
    }

    [Fact]
    public async Task PersistedBuckets_ContainNoRawIdentifiers()
    {
        using var scopeFactory = CreateRateLimitFactory();
        var client = scopeFactory.CreateClient();

        await PostAsync(client, "api/auth/password-reset/request", new { email = "secret.person@shop.test" }, peer: "198.51.100.77");
        await PostAsync(client, "api/auth/login/email", new { email = "secret.person@shop.test", password = "wrong-password" }, peer: "198.51.100.77");

        var rows = await ReadBucketsAsync();
        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
        {
            Assert.DoesNotContain("secret.person", row, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("198.51.100.77", row, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task MissingBody_StillConsumesAddressBudget()
    {
        using var scopeFactory = CreateRateLimitFactory();
        var client = scopeFactory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
            statuses.Add((await PostAsync(client, "api/auth/login/email", new { }, peer: "198.51.100.90")).StatusCode);

        Assert.All(statuses.Take(10), s => Assert.Equal(HttpStatusCode.BadRequest, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }

    [Fact]
    public async Task UntrustedForwardedFor_DoesNotCreateFreshBucket()
    {
        using var scopeFactory = CreateRateLimitFactory(new()
        {
            ["Proxy:Enabled"] = "true",
            ["Proxy:KnownProxies:0"] = "10.0.0.1",
        });
        var client = scopeFactory.CreateClient();

        var spoofed = new List<HttpStatusCode>();
        foreach (var forged in new[] { "1.1.1.1", "2.2.2.2", "3.3.3.3" })
            spoofed.Add((await GetAsync(client, "api/rate-limit-test/default", peer: "198.51.100.7", forwardedFor: forged)).StatusCode);

        var viaProxyFirst = await GetAsync(client, "api/rate-limit-test/default", peer: "10.0.0.1", forwardedFor: "192.0.2.10");
        var viaProxySecond = await GetAsync(client, "api/rate-limit-test/default", peer: "10.0.0.1", forwardedFor: "192.0.2.11");
        var viaProxyThird = await GetAsync(client, "api/rate-limit-test/default", peer: "10.0.0.1", forwardedFor: "192.0.2.12");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], spoofed);
        Assert.Equal(HttpStatusCode.OK, viaProxyFirst.StatusCode);
        Assert.Equal(HttpStatusCode.OK, viaProxySecond.StatusCode);
        Assert.Equal(HttpStatusCode.OK, viaProxyThird.StatusCode);
    }

    /// <summary>
    /// The deployed shape: Container Apps ingress in front of the web container's proxy, so two
    /// trusted hops, from addresses that are not published. Only the two right-most entries are
    /// read; whatever a caller writes to the left of them is not where it is counted from.
    /// </summary>
    [Fact]
    public async Task DeployedTrustModel_ForgedLeadingEntries_ShareBucket()
    {
        using var scopeFactory = CreateRateLimitFactory(new()
        {
            ["Proxy:Enabled"] = "true",
            ["Proxy:TrustAnyProxy"] = "true",
            ["Proxy:ForwardLimit"] = "2",
        });
        var client = scopeFactory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        foreach (var forged in new[] { "1.1.1.1", "2.2.2.2", "3.3.3.3" })
            statuses.Add((await GetAsync(client, "api/rate-limit-test/default", peer: "10.0.0.1", forwardedFor: $"{forged}, 198.51.100.7, 10.0.0.2")).StatusCode);
        var anotherBrowser = await GetAsync(client, "api/rate-limit-test/default", peer: "10.0.0.1", forwardedFor: "198.51.100.8, 10.0.0.2");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.OK, anotherBrowser.StatusCode);
    }

    [Fact]
    public async Task AuthBudgetStoreUnavailable_Returns503WithoutIssuance()
    {
        // Nothing listens on port 1: every acquisition fails to connect, as it would with the
        // database down.
        var unreachable = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=nobody;Password=nobody;Database=nothing;Timeout=1;Pooling=false");
        try
        {
            using var scopeFactory = CreateRateLimitFactory(configureServices: services =>
            {
                services.RemoveAll<IAtomicRateLimitStore>();
                services.AddSingleton<IAtomicRateLimitStore>(new PostgresRateLimitStore(
                    unreachable, Microsoft.Extensions.Options.Options.Create(new RateLimitStoreOptions { CommandTimeoutSeconds = 1 })));
            });
            var client = scopeFactory.CreateClient();
            var userId = await SeedVerifiedUserAsync(scopeFactory, "outage@shop.test");

            var reset = await PostAsync(client, "api/auth/password-reset/request", new { email = "outage@shop.test" }, peer: "198.51.100.20");
            var login = await PostAsync(client, "api/auth/login/email", new { email = "outage@shop.test", password = Password }, peer: "198.51.100.20");
            var business = await GetAsync(client, "api/rate-limit-test/default", peer: "198.51.100.20");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, reset.StatusCode);
            Assert.Equal("RateLimit.Unavailable", (await reset.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
            Assert.Equal(0, await CountResetTokensAsync(scopeFactory, userId));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, login.StatusCode);
            Assert.DoesNotContain("accessToken", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(HttpStatusCode.OK, business.StatusCode);
        }
        finally
        {
            await unreachable.DisposeAsync();
        }
    }

    private WebApplicationFactory<Program> CreateRateLimitFactory(
        Dictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? configureServices = null)
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
                if (configuration is not null)
                    configBuilder.AddInMemoryCollection(configuration);
            });

            builder.ConfigureServices(services =>
            {
                services.AddControllers()
                    .AddApplicationPart(typeof(RateLimitTestController).Assembly);

                services.RemoveAll<IAtomicRateLimitStore>();
                services.AddSingleton<IAtomicRateLimitStore, PostgresRateLimitStore>();
                configureServices?.Invoke(services);
            });
        });

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body, string peer)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add(RateLimitTestPeers.Header, peer);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string url, string peer, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(RateLimitTestPeers.Header, peer);
        if (forwardedFor is not null)
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }

    /// <summary>
    /// Everything a caller can observe except the per-request trace id: status, the limit headers,
    /// and the body's title, detail and message, with Retry-After apart because it is a countdown.
    /// </summary>
    private static async Task<(string Visible, int? RetryAfter)> PublicOutcomeAsync(HttpResponseMessage response)
    {
        static string Header(HttpResponseMessage r, string name) =>
            r.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : "-";

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        static string Field(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) ? value.ToString() : "-";

        var visible = string.Join(
            "|",
            ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Header(response, "X-RateLimit-Limit"),
            Header(response, "X-RateLimit-Remaining"),
            Field(body.RootElement, "title"),
            Field(body.RootElement, "detail"),
            Field(body.RootElement, "message"));
        var retryAfter = response.Headers.RetryAfter?.Delta is { } delta ? (int?)delta.TotalSeconds : null;
        return (visible, retryAfter);
    }

    private static async Task<Guid> SeedVerifiedUserAsync(WebApplicationFactory<Program> factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = User.CreateWithEmail(email, hasher.Hash(Password), "Rate", "Limit");
        user.VerifyEmail(email);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<int> CountResetTokensAsync(WebApplicationFactory<Program> factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Set<PasswordResetToken>().CountAsync(x => x.UserId == userId);
    }

    private async Task<List<string>> ReadBucketsAsync()
    {
        await using var connection = new NpgsqlConnection(_factory.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT policy_id || ' ' || bucket_key FROM rate_limit_buckets", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(reader.GetString(0));
        return rows;
    }
}

[ApiController]
[Route("api/rate-limit-test")]
public sealed class RateLimitTestController : ControllerBase
{
    [HttpGet("default")]
    public IActionResult Default() => Ok(new { Status = "ok" });

    [HttpGet("items/{id}")]
    public IActionResult Item(int id) => Ok(new { Id = id });

    [DisableRateLimit]
    [HttpGet("disabled")]
    public IActionResult Disabled() => Ok(new { Status = "ok" });
}
