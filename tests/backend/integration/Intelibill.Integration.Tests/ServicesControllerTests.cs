using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ServicesControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string UniqueEmail() => $"services-{Guid.NewGuid():N}@test.com";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<string> CreateShopAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task ServicesLifecycle_AndDuplicateHardening_WorksEndToEnd()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/services");
        addRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        addRequest.Content = JsonContent.Create(new
        {
            name = "Bike Wash",
            description = "Quick wash",
            price = 150m,
            hsnCode = "9987",
            taxRatePercent = 18m,
            taxIncluded = false,
            isActive = true,
        });

        var addResponse = await client.SendAsync(addRequest);
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);
        var added = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var serviceId = added.GetProperty("serviceId").GetGuid();

        using var duplicateNameRequest = new HttpRequestMessage(HttpMethod.Post, "/api/services");
        duplicateNameRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        duplicateNameRequest.Content = JsonContent.Create(new
        {
            name = "Bike Wash",
            description = "Duplicate",
            price = 200m,
            hsnCode = "9987",
            taxRatePercent = 18m,
            taxIncluded = false,
            isActive = true,
        });

        var duplicateNameResponse = await client.SendAsync(duplicateNameRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateNameResponse.StatusCode);

        using var updateRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/services/{serviceId}");
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        updateRequest.Content = JsonContent.Create(new
        {
            name = "Bike Wash Premium",
            description = "With polish",
            price = 250m,
            hsnCode = "9987",
            taxRatePercent = 18m,
            taxIncluded = false,
        });

        var updateResponse = await client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        using var deactivateRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/services/{serviceId}/deactivate");
        deactivateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var deactivateResponse = await client.SendAsync(deactivateRequest);
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);

        using var activeOnlyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/services?includeInactive=false");
        activeOnlyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var activeOnlyResponse = await client.SendAsync(activeOnlyRequest);
        Assert.Equal(HttpStatusCode.OK, activeOnlyResponse.StatusCode);
        var activeOnly = await activeOnlyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(activeOnly.EnumerateArray(), x => x.GetProperty("serviceId").GetGuid() == serviceId);

        using var activateRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/services/{serviceId}/activate");
        activateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var activateResponse = await client.SendAsync(activateRequest);
        Assert.Equal(HttpStatusCode.NoContent, activateResponse.StatusCode);

        using var searchRequest = new HttpRequestMessage(HttpMethod.Get, "/api/services?search=premium");
        searchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var searchResponse = await client.SendAsync(searchRequest);
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var search = await searchResponse.Content.ReadFromJsonAsync<JsonElement>();
        var matched = Assert.Single(search.EnumerateArray(), x => x.GetProperty("serviceId").GetGuid() == serviceId);
        Assert.Equal("Bike Wash Premium", matched.GetProperty("name").GetString());
        Assert.True(matched.GetProperty("isActive").GetBoolean());
    }
}
