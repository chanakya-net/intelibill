using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

/// <summary>
/// A protected answer belongs to the identity that asked for it; a browser cache keyed by URL must
/// not keep it for the next reader. Checked through the real pipeline, so the order of the
/// middleware that writes refusals is part of what is pinned.
/// </summary>
[Collection("Integration Tests")]
public sealed class ProtectedResponseCacheIntegrationTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task UnauthenticatedApiRefusal_IsNoStore()
    {
        using var factory = new ApiWebApplicationFactory(fixture);
        await factory.InitializeAsync();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/shops/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore, "A 401 under /api must be no-store.");
    }

    [Fact]
    public async Task AuthenticatedApiAnswer_IsNoStore()
    {
        using var factory = new ApiWebApplicationFactory(fixture);
        await factory.InitializeAsync();
        using var client = CreateClient(factory);

        var enrolled = await client.EnrollAsync(new
        {
            email = $"no-store-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Cache",
            lastName = "Probe",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
        });
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        var accessToken = (await enrolled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/shops/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore, "A protected 200 must be no-store.");
    }

    [Fact]
    public async Task UnprotectedDocument_KeepsItsOwnPolicy()
    {
        // Health probes already refuse caching on their own, so the API description served in
        // Development stands in for a path outside /api and /hubs.
        using var factory = new ApiWebApplicationFactory(fixture);
        await factory.InitializeAsync();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(true, response.Headers.CacheControl?.NoStore);
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
}
