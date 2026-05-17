using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests.Features.Hsn;

[Collection("Integration Tests")]
public sealed class HsnLookupTests : IAsyncLifetime, IDisposable
{
    private readonly FakeHsnServiceState _hsnState;
    private readonly ApiWebApplicationFactory _factory;

    public HsnLookupTests(PostgreSqlTestFixture fixture)
    {
        _hsnState = new FakeHsnServiceState();
        _factory = new ApiWebApplicationFactory(
            fixture,
            services =>
            {
                services.AddSingleton(_hsnState);
                services.AddHttpClient("HsnService")
                    .ConfigurePrimaryHttpMessageHandler(sp => new FakeHsnHttpMessageHandler(sp.GetRequiredService<FakeHsnServiceState>()));
            });
    }

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
        AllowAutoRedirect = false
    });

    private static string UniqueEmail() => $"hsn-{Guid.NewGuid():N}@test.com";

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
            pincode = "560001"
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static HttpRequestMessage AuthedPost(string path, string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    [Fact]
    public async Task LookupHsn_Unauthenticated_Returns401()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/hsn/lookup", new { productName = "Paracetamol" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LookupHsn_CacheMiss_CallsExternalApiAndReturnsResult()
    {
        _hsnState.Responder = _ => FakeHsnServiceState.JsonOk(FakeHsnServiceState.SingleResultJson);

        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));

        using var request = AuthedPost("/api/hsn/lookup", token, new { productName = "Paracetamol" });
        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("30049069", body.GetProperty("hsnCodes")[0].GetString());
        Assert.Equal("Standard", body.GetProperty("taxScenarios")[0].GetProperty("condition").GetString());

        Assert.Equal(1, _hsnState.CallCount);
    }

    [Fact]
    public async Task LookupHsn_CacheMiss_SavesResultToDatabase()
    {
        _hsnState.Responder = _ => FakeHsnServiceState.JsonOk(FakeHsnServiceState.SingleResultJson);

        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));

        using var response = await client.SendAsync(AuthedPost("/api/hsn/lookup", token, new { productName = "Paracetamol" }));
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var count = await db.HsnCaches.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task LookupHsn_CacheHit_ReturnsFromDatabaseWithoutCallingApi()
    {
        _hsnState.Responder = _ => FakeHsnServiceState.JsonOk(FakeHsnServiceState.SingleResultJson);

        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));

        using var response1 = await client.SendAsync(AuthedPost("/api/hsn/lookup", token, new { productName = "Paracetamol" }));
        response1.EnsureSuccessStatusCode();
        Assert.Equal(1, _hsnState.CallCount);

        _hsnState.Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        using var response2 = await client.SendAsync(AuthedPost("/api/hsn/lookup", token, new { productName = "Paracetamol" }));
        response2.EnsureSuccessStatusCode();
        Assert.Equal(1, _hsnState.CallCount);
    }

    [Fact]
    public async Task LookupHsn_EmptyProductName_Returns400()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));

        using var response = await client.SendAsync(AuthedPost("/api/hsn/lookup", token, new { productName = "" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _hsnState.CallCount);
    }

    [Fact]
    public async Task LookupHsn_MultipleHsnCodes_ReturnsAll()
    {
        _hsnState.Responder = _ => FakeHsnServiceState.JsonOk(FakeHsnServiceState.MultipleHsnCodesJson);

        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));

        using var response = await client.SendAsync(AuthedPost("/api/hsn/lookup", token, new { productName = "Paracetamol" }));
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("hsnCodes").GetArrayLength());
        Assert.Equal("30049069", body.GetProperty("hsnCodes")[0].GetString());
        Assert.Equal("30049071", body.GetProperty("hsnCodes")[1].GetString());
    }

    [Fact]
    public async Task LookupHsn_MultipleTaxScenarios_ReturnsAll()
    {
        _hsnState.Responder = _ => FakeHsnServiceState.JsonOk(FakeHsnServiceState.MultipleTaxScenariosJson);

        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));

        using var response = await client.SendAsync(AuthedPost("/api/hsn/lookup", token, new { productName = "Paracetamol" }));
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("taxScenarios").GetArrayLength());
    }

    internal sealed class FakeHsnServiceState
    {
        public const string SingleResultJson =
            "{\"success\":true,\"data\":{\"name\":\"Paracetamol\",\"hsn\":[\"30049069\"],\"taxScenarios\":[{\"condition\":\"Standard\",\"taxPercentage\":\"5%\"}]},\"error\":null}";

        public const string MultipleHsnCodesJson =
            "{\"success\":true,\"data\":{\"name\":\"Paracetamol\",\"hsn\":[\"30049069\",\"30049071\"],\"taxScenarios\":[{\"condition\":\"Standard\",\"taxPercentage\":\"5%\"}]},\"error\":null}";

        public const string MultipleTaxScenariosJson =
            "{\"success\":true,\"data\":{\"name\":\"Paracetamol\",\"hsn\":[\"30049069\"],\"taxScenarios\":[{\"condition\":\"Standard\",\"taxPercentage\":\"5%\"},{\"condition\":\"Luxury\",\"taxPercentage\":\"18%\"}]},\"error\":null}";

        public int CallCount { get; set; }

        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        public static HttpResponseMessage JsonOk(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    internal sealed class FakeHsnHttpMessageHandler(FakeHsnServiceState state) : HttpMessageHandler
    {
        private readonly FakeHsnServiceState _state = state;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _state.CallCount++;
            return Task.FromResult(_state.Responder(request));
        }
    }
}
