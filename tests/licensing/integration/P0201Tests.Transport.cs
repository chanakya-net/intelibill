using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0201Tests
{
    [Fact]
    public async Task DelegationPayload_ContainsOnlyApprovedFacts()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        fixture.Clock.Now = DateTimeOffset.UtcNow;
        await using var licensing = fixture.LicensingApi();
        using var licensingClient = licensing.CreateClient();
        using var capture = new CapturingTransport(licensing.Server.CreateHandler());
        await using var merchant = fixture.MerchantApi(handler: () => capture,
            configure: services => services.AddSingleton<TimeProvider>(fixture.Clock));
        using var client = merchant.CreateClient();
        var actor = await SeedMerchantAsync(merchant.Services);
        var jwt = await LoginAsync(client);
        var sessionExpiry = fixture.Clock.Now.ToUnixTimeSeconds() + 33;
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), $"""
            UPDATE auth_sessions SET expires_at = to_timestamp({sessionExpiry}) WHERE user_id = '{actor:D}'
            """);
        var account = Guid.NewGuid();
        await fixture.GrantAsync(account, actor);

        var injected = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get, MerchantPath(account));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        request.Headers.Add(P0201Fixture.RequestHeader, injected.ToString("D"));
        request.Headers.Add("X-Actor-Id", injected.ToString("D"));
        request.Headers.Add("Cookie", "fixture-browser-cookie=1");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, capture.Calls);
        Assert.Equal(new[] { "Authorization", P0201Fixture.RequestHeader }, capture.Headers);
        Assert.Equal(P0201Fixture.InternalPath(account).OriginalString, capture.Path);
        Assert.False(capture.DelegationToken == jwt);

        var parts = capture.DelegationToken!.Split('.');
        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
        using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[0]));
        await AssertWireFieldsAsync(header.RootElement, payload.RootElement);
        var facts = payload.RootElement;
        Assert.Equal(actor, facts.GetProperty("sub").GetGuid());
        Assert.Equal(account, facts.GetProperty("account_id").GetGuid());
        Assert.True(facts.GetProperty("verified_identity").GetBoolean());
        Assert.Equal(fixture.Clock.Now.ToUnixTimeSeconds(), facts.GetProperty("iat").GetInt64());
        Assert.Equal(sessionExpiry, facts.GetProperty("exp").GetInt64());
        Assert.NotEqual(injected, facts.GetProperty("request_id").GetGuid());
        Assert.Equal(capture.RequestId, facts.GetProperty("request_id").GetGuid());
        Assert.NotEqual(capture.RequestId, facts.GetProperty("jti").GetGuid());
        Assert.False((await response.Content.ReadAsStringAsync()).Contains(capture.DelegationToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TransportAndConfiguration_FailClosedWithoutCredentialLeaks()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        var status = HttpStatusCode.Redirect;
        var content = "{";
        var transport = new LocalTransport(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json"),
            Headers = { Location = new Uri("https://never-contact.test/") },
        });
        await using var merchant = fixture.MerchantApi(handler: () => transport);
        using var client = merchant.CreateClient();
        await SeedMerchantAsync(merchant.Services);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client));

        await AssertUnavailableAsync(client);
        status = HttpStatusCode.Unauthorized;
        await AssertUnavailableAsync(client);
        status = HttpStatusCode.OK;
        await AssertUnavailableAsync(client);
        content = new string('x', 8192);
        await AssertUnavailableAsync(client);
        Assert.Equal(4, transport.Calls);

        // Reuse an actual live merchant JWT with a different host environment.
        await using var production = fixture.MerchantApi(handler: () => transport, environment: "Production");
        using var productionClient = production.CreateClient();
        productionClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        await AssertUnavailableAsync(productionClient);
        Assert.Equal(4, transport.Calls);

        await using var unconfigured = new MerchantStartupProbeFactory(fixture.MerchantRuntime, "Development");
        using var noConfig = unconfigured.CreateClient();
        noConfig.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        await AssertUnavailableAsync(noConfig);
        using var ordinaryAccess = await noConfig.GetAsync(new Uri("/api/shops/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, ordinaryAccess.StatusCode);
        Assert.Equal(0L, await ReplayCountAsync(fixture));
    }

    private static async Task AssertWireFieldsAsync(JsonElement header, JsonElement payload)
    {
        using var contract = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/merchant-billing-delegation-v1.json")));
        var rules = contract.RootElement;
        Assert.Equal(FieldNames(rules, "headerFields"), Names(header));
        Assert.Equal(FieldNames(rules, "payloadFields"), Names(payload));
        Assert.Equal(rules.GetProperty("issuer").GetString(), payload.GetProperty("iss").GetString());
        Assert.Equal(rules.GetProperty("audience").GetString(), payload.GetProperty("aud").GetString());
        Assert.Equal(rules.GetProperty("type").GetString(), header.GetProperty("typ").GetString());
    }

    private static string?[] FieldNames(JsonElement contract, string name) =>
        [.. contract.GetProperty(name).EnumerateArray().Select(field => field.GetString())];

    private static string[] Names(JsonElement element) =>
        [.. element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

    private static async Task AssertUnavailableAsync(HttpClient client)
    {
        using var response = await client.GetAsync(MerchantPath(Guid.NewGuid()));
        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "Billing.DelegationUnavailable");
    }

    private sealed class LocalTransport(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class CapturingTransport(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        internal int Calls { get; private set; }
        internal string? DelegationToken { get; private set; }
        internal string? Path { get; private set; }
        internal Guid RequestId { get; private set; }
        internal string[] Headers { get; private set; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Headers = [.. request.Headers.Select(header => header.Key).Order(StringComparer.Ordinal)];
            Path = request.RequestUri!.PathAndQuery;
            DelegationToken = request.Headers.Authorization?.Parameter;
            RequestId = Guid.Parse(Assert.Single(request.Headers.GetValues(P0201Fixture.RequestHeader)));
            return base.SendAsync(request, cancellationToken);
        }
    }
}
