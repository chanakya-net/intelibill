using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0202Tests
{
    [Fact]
    public async Task SameLoginAndName_AreNotGrouped()
    {
        await using var host = await MerchantHostAsync();
        var a = await host.SignInAsync();
        await SeedShopAsync(a.Actor, ShopRole.Owner, isDefault: true, gstin: "29ABCDE1234F1Z5");
        await SeedShopAsync(a.Actor, ShopRole.Owner, isDefault: false, gstin: "29ABCDE1234F1Z5");
        var b = await host.SignInAsync();
        a.Client.DefaultRequestHeaders.Add("X-Actor-Id", b.Actor.ToString("D"));
        a.Client.DefaultRequestHeaders.Add("X-Billing-Request-Id", Guid.NewGuid().ToString("D"));

        var first = await CreateThroughMerchantAsync(a.Client, Guid.NewGuid(), "Shared Name");
        var second = await CreateThroughMerchantAsync(a.Client, Guid.NewGuid(), "Shared Name");
        var other = await CreateThroughMerchantAsync(b.Client, Guid.NewGuid(), "Shared Name");
        Assert.Equal(3, new[] { first, second, other }.Distinct().Count());
        Assert.Equal(new[] { first, second }.Order(), (await ListThroughMerchantAsync(a.Client)).Order());
        Assert.Equal([other], await ListThroughMerchantAsync(b.Client));

        var rows = await P0202Fixture.ReadAccountRowsAsync(database, host.Fixture);
        Assert.Equal(3, rows.Length);
        Assert.Equal(new[] { first, second }.Order(), rows.Where(row => row.MerchantUserId == a.Actor).Select(row => row.AccountId).Order());
        Assert.Equal(other, Assert.Single(rows, row => row.MerchantUserId == b.Actor).AccountId);
        Assert.All(rows, row => Assert.Equal((1, 1), (row.Role, row.Status)));
        Assert.Equal(0L, await P0202Fixture.CountAsync(database, host.Fixture, """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name IN ('billing_accounts', 'billing_account_members')
              AND column_name ~ '(shop|email|phone|gstin|login|customer)'
            """));
        AssertWireMatchesContract(host.Capture, a.Actor);
    }

    [Fact]
    public async Task BillingMembership_GrantsNoMerchantShopAccess()
    {
        await using var host = await MerchantHostAsync();
        var a = await host.SignInAsync();
        var b = await host.SignInAsync();
        var foreignShop = await SeedShopAsync(b.Actor, ShopRole.Owner, isDefault: true);
        var memberships = await CountMerchantAsync("SELECT count(*) FROM shop_memberships");
        var before = await a.Client.GetStringAsync(new Uri("/api/shops/me", UriKind.Relative));

        var account = await CreateThroughMerchantAsync(a.Client, Guid.NewGuid(), "Billing only");
        using (var access = await a.Client.GetAsync(new Uri($"/api/billing/accounts/{account:D}/access", UriKind.Relative)))
            Assert.Equal(HttpStatusCode.OK, access.StatusCode);
        using (var denied = await b.Client.GetAsync(new Uri($"/api/billing/accounts/{account:D}/access", UriKind.Relative)))
            await AssertProblemAsync(denied, HttpStatusCode.Forbidden, "Billing.AccountAccessDenied");

        Assert.Equal(before, await a.Client.GetStringAsync(new Uri("/api/shops/me", UriKind.Relative)));
        using (var shop = await a.Client.GetAsync(new Uri($"/api/shops/{foreignShop:D}", UriKind.Relative)))
            Assert.NotEqual(HttpStatusCode.OK, shop.StatusCode);
        using (var switched = await a.Client.PostAsJsonAsync(new Uri("/api/shops/switch", UriKind.Relative),
            new { shopId = foreignShop }))
            Assert.NotEqual(HttpStatusCode.OK, switched.StatusCode);
        Assert.Equal(memberships, await CountMerchantAsync("SELECT count(*) FROM shop_memberships"));
    }

    [Fact]
    public async Task InvalidCreate_SendsNoDelegation()
    {
        await using var host = await MerchantHostAsync();
        var a = await host.SignInAsync();
        var invalid = new (object Body, string Code)[]
        {
            (new { operationKey = Guid.NewGuid(), name = "   " }, "Billing.AccountNameInvalid"),
            (new { operationKey = Guid.NewGuid(), name = new string('x', 121) }, "Billing.AccountNameInvalid"),
            (new { operationKey = Guid.NewGuid(), name = "Tab\tname" }, "Billing.AccountNameInvalid"),
            (new { operationKey = Guid.Empty, name = "Acme" }, "Billing.OperationKeyInvalid"),
        };
        foreach (var (body, code) in invalid)
        {
            using var response = await a.Client.PostAsJsonAsync(MerchantAccounts, body);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
        }

        using var anonymous = host.Merchant.CreateClient();
        using (var list = await anonymous.GetAsync(MerchantAccounts))
            await AssertProblemAsync(list, HttpStatusCode.Unauthorized, "Billing.SessionInvalid");
        using (var create = await anonymous.PostAsJsonAsync(MerchantAccounts, new { operationKey = Guid.NewGuid(), name = "Acme" }))
            await AssertProblemAsync(create, HttpStatusCode.Unauthorized, "Billing.SessionInvalid");
        Assert.Empty(host.Capture);
        Assert.Equal(0L, await P0202Fixture.CountAsync(database, host.Fixture, UseCount));
    }

    [Fact]
    public async Task CreateBudget_IsEnforcedBeforeDelegation()
    {
        await using var host = await MerchantHostAsync();
        var a = await host.SignInAsync();
        for (var attempt = 0; attempt < 10; attempt++)
            await CreateThroughMerchantAsync(a.Client, Guid.NewGuid(), $"Account {attempt}");

        using var limited = await a.Client.PostAsJsonAsync(MerchantAccounts, new { operationKey = Guid.NewGuid(), name = "Eleventh" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(10, host.Capture.Count);
        Assert.Equal(10, (await ListThroughMerchantAsync(a.Client)).Length);
    }

    private static void AssertWireMatchesContract(ConcurrentQueue<CapturedDelegation> capture, Guid actor)
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/billing-accounts-v1.json")));
        var root = contract.RootElement;
        var fields = root.GetProperty("actorScopedPayloadFields").EnumerateArray().Select(field => field.GetString()).ToArray();
        var headers = root.GetProperty("outboundHeaders").EnumerateArray().Select(name => name.GetString()).ToArray();
        var bodyFields = root.GetProperty("bindings").GetProperty("create").GetProperty("bodyFields")
            .EnumerateArray().Select(field => field.GetString()).ToArray();
        var ofActor = capture.Where(item => item.Payload.GetProperty("sub").GetGuid() == actor).ToArray();
        Assert.Contains(ofActor, item => item.Method == "POST");
        Assert.Contains(ofActor, item => item.Method == "GET");
        foreach (var item in ofActor)
        {
            var binding = root.GetProperty("bindings").GetProperty(item.Method == "POST" ? "create" : "list");
            Assert.Equal(fields, item.Payload.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));
            Assert.Equal(headers, item.Headers.Order(StringComparer.Ordinal));
            Assert.Equal(binding.GetProperty("action").GetString(), item.Payload.GetProperty("action").GetString());
            Assert.Equal(binding.GetProperty("method").GetString(), item.Payload.GetProperty("method").GetString());
            Assert.Equal(binding.GetProperty("path").GetString(), item.Path);
            Assert.Equal(item.Path, item.Payload.GetProperty("path").GetString());
            Assert.Equal(item.BodySha256, item.Payload.GetProperty("body_sha256").GetString());
            Assert.NotEqual(item.RequestHeaderId, Guid.Empty);
            Assert.Equal(item.RequestHeaderId, item.Payload.GetProperty("request_id").GetGuid());
            if (item.Method == "POST")
            {
                using var body = JsonDocument.Parse(item.Body);
                Assert.Equal(bodyFields, body.RootElement.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));
            }
            else
            {
                Assert.Equal(binding.GetProperty("bodySha256").GetString(), item.BodySha256);
            }
        }
    }

    private async Task<MerchantHost> MerchantHostAsync()
    {
        await database.PrepareMerchantAsync();
        var fixture = await P0201Fixture.CreateAsync(database);
        fixture.Clock.Now = P0202Fixture.WholeSecondNow();
        var licensing = P0202Fixture.LicensingApi(fixture);
        var capture = new ConcurrentQueue<CapturedDelegation>();
        var merchant = fixture.MerchantApi(
            handler: () => new CapturingHandler(capture) { InnerHandler = licensing.Server.CreateHandler() },
            configure: services => services.AddSingleton<TimeProvider>(fixture.Clock));
        return new MerchantHost(fixture, licensing, merchant, capture);
    }

    private async Task<Guid> SeedShopAsync(Guid actor, ShopRole role, bool isDefault, string? gstin = null)
    {
        await using var db = MerchantOwnerContext();
        var shop = Shop.Create("Billing fixture shop", "Fixture Road", "Bengaluru", "Karnataka", "560001", null, null, gstin);
        db.Shops.Add(shop);
        db.ShopMemberships.Add(ShopMembership.Create(shop.Id, actor, role, isDefault));
        await db.SaveChangesAsync();
        return shop.Id;
    }

    private async Task<long> CountMerchantAsync(string sql) => (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(
        database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), sql))!;

    private ApplicationDbContext MerchantOwnerContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase).ConnectionString)
        .UseSnakeCaseNamingConvention().Options);

    private static async Task<Guid> CreateThroughMerchantAsync(HttpClient client, Guid operationKey, string name)
    {
        using var response = await client.PostAsJsonAsync(MerchantAccounts, new { operationKey, name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountId").GetGuid();
    }

    private static async Task<Guid[]> ListThroughMerchantAsync(HttpClient client)
    {
        using var response = await client.GetAsync(MerchantAccounts);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accounts");
        return [.. accounts.EnumerateArray().Select(account => account.GetProperty("accountId").GetGuid())];
    }

    internal sealed record CapturedDelegation(string Method, string Path, string[] Headers, Guid RequestHeaderId,
        JsonElement Payload, byte[] Body, string BodySha256);

    private sealed class CapturingHandler(ConcurrentQueue<CapturedDelegation> capture) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var token = request.Headers.Authorization?.Parameter ?? string.Empty;
            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]));
            var requestId = request.Headers.TryGetValues(P0201Fixture.RequestHeader, out var ids) && ids.Count() == 1
                ? Guid.Parse(ids.Single()) : Guid.Empty;
            capture.Enqueue(new CapturedDelegation(request.Method.Method, request.RequestUri!.AbsolutePath,
                [.. request.Headers.Select(header => header.Key)], requestId, payload.RootElement.Clone(), body,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(body))));
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class MerchantHost(P0201Fixture fixture, LicensingApiFactory licensing,
        MerchantStartupProbeFactory merchant, ConcurrentQueue<CapturedDelegation> capture) : IAsyncDisposable
    {
        private readonly List<HttpClient> _clients = [];
        internal P0201Fixture Fixture => fixture;
        internal MerchantStartupProbeFactory Merchant => merchant;
        internal ConcurrentQueue<CapturedDelegation> Capture => capture;

        internal async Task<(Guid Actor, HttpClient Client)> SignInAsync()
        {
            var client = merchant.CreateClient();
            _clients.Add(client);
            var email = P0202Fixture.UniqueEmail();
            var actor = await P0201Tests.SeedMerchantAsync(merchant.Services, email);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await P0201Tests.LoginAsync(client, email));
            return (actor, client);
        }

        public async ValueTask DisposeAsync()
        {
            _clients.ForEach(client => client.Dispose());
            await merchant.DisposeAsync();
            await licensing.DisposeAsync();
            fixture.Dispose();
        }
    }
}
