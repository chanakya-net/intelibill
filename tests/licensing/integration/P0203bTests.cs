using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed partial class P0203bTests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task OwnerRemovesAcceptedManager_RecordsEvidenceAndPreservesOwner()
    {
        await using var scenario = await StartAsync();
        using var removed = await scenario.RemoveAsync();
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var result = await ReadAsync(removed);
        Assert.Equal("Removed", result.GetProperty("status").GetString());
        Assert.Equal(2, result.GetProperty("revision").GetInt32());
        Assert.Equal(scenario.Member, result.GetProperty("memberId").GetGuid());
        Assert.Equal(1L, await scenario.CountAsync("SELECT count(*) FROM billing_member_removals"));
        var roster = await scenario.Owner.GetFromJsonAsync<JsonElement>(scenario.Path + "/members");
        Assert.Equal("Owner", Assert.Single(roster.GetProperty("members").EnumerateArray()).GetProperty("role").GetString());
    }

    [Fact]
    public async Task RevokedManager_ExistingSessionCannotReadOrActOnAccount()
    {
        await using var scenario = await StartAsync();
        var manager = scenario.Manager;
        var shopAccess = await manager.GetStringAsync("/api/shops/me");
        Assert.Contains(scenario.Shop.ToString("D"), shopAccess, StringComparison.OrdinalIgnoreCase);
        var other = await CreateAsync(manager);
        var action = new { operationKey = Guid.NewGuid(), shopId = scenario.Shop, withoutCoverage = true };
        var paths = new[] { "/access", "/members", "/affiliations" };
        foreach (var path in paths)
        {
            using var allowed = await manager.GetAsync(scenario.Path + path);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var before = await manager.PostAsJsonAsync(scenario.Path + "/affiliations", action);
        Assert.Equal(HttpStatusCode.Created, before.StatusCode);
        var request = (await ReadAsync(before)).GetProperty("requestId").GetGuid();
        var consent = $"{scenario.Path}/affiliations/{scenario.Shop:D}/{request:D}/consent";
        using var removed = await scenario.RemoveAsync();
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        foreach (var path in paths)
            await AssertDeniedAsync(await manager.GetAsync(scenario.Path + path));
        await AssertDeniedAsync(await manager.PostAsJsonAsync(scenario.Path + "/affiliations", action));
        await AssertDeniedAsync(await manager.PostAsJsonAsync(scenario.Path + "/affiliations", action with { operationKey = Guid.NewGuid() }));

        using var shopConsent = await manager.GetAsync(consent);
        Assert.Equal(HttpStatusCode.OK, shopConsent.StatusCode);
        var list = await manager.GetFromJsonAsync<JsonElement>("/api/billing/accounts");
        Assert.Equal(other, Assert.Single(list.GetProperty("accounts").EnumerateArray()).GetProperty("accountId").GetGuid());
        Assert.Equal(shopAccess, await manager.GetStringAsync("/api/shops/me"));
        Assert.Equal(scenario.ShopAuthorities, await ShopAuthoritiesAsync(database));
    }

    [Fact]
    public async Task RemovalRetry_ReplaysOnlyForLiveOwnerAndOriginalPayload()
    {
        await using var scenario = await StartAsync();
        await AssertDeniedAsync(await scenario.RemoveAsync(caller: scenario.Manager));
        await AssertCodeAsync(await scenario.RemoveAsync(member: scenario.OwnerMember), HttpStatusCode.Conflict, "Billing.OwnerRemovalForbidden");
        await AssertCodeAsync(await scenario.RemoveAsync(member: Guid.NewGuid()), HttpStatusCode.NotFound, "Billing.MemberUnavailable");
        await AssertCodeAsync(await scenario.RemoveAsync(version: 2), HttpStatusCode.Conflict, "Billing.MemberVersionConflict");

        using var first = await scenario.RemoveAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body = await first.Content.ReadAsStringAsync();
        using var replay = await scenario.RemoveAsync();
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(body, await replay.Content.ReadAsStringAsync());
        await AssertDeniedAsync(await scenario.RemoveAsync(caller: scenario.Manager));
        await AssertCodeAsync(await scenario.RemoveAsync(reason: "Changed"), HttpStatusCode.Conflict, "Billing.OperationPayloadMismatch");
        await AssertCodeAsync(await scenario.RemoveAsync(member: Guid.NewGuid()), HttpStatusCode.Conflict, "Billing.OperationPayloadMismatch");
        await AssertCodeAsync(await scenario.RemoveAsync(key: Guid.NewGuid()), HttpStatusCode.Conflict, "Billing.MemberVersionConflict");
        Assert.Equal(1L, await scenario.CountAsync("SELECT count(*) FROM billing_member_removals"));
    }

    [Fact]
    public async Task ConcurrentRemoval_OnlyOneTransitionCommits()
    {
        await using var scenario = await StartAsync();
        var results = await Task.WhenAll(scenario.RemoveAsync(), scenario.RemoveAsync(), scenario.RemoveAsync(key: Guid.NewGuid()));
        using var first = results[0];
        using var replay = results[1];
        using var distinct = results[2];
        var winner = first.StatusCode == HttpStatusCode.OK ? first : distinct;
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        Assert.Equal(first.StatusCode, replay.StatusCode);
        Assert.Equal(first.StatusCode == HttpStatusCode.OK ? HttpStatusCode.Conflict : HttpStatusCode.OK, distinct.StatusCode);
        Assert.Equal(1L, await scenario.CountAsync("SELECT count(*) FROM billing_member_removals"));
        Assert.Equal(1L, await scenario.CountAsync("SELECT count(*) FROM billing_account_members WHERE role = 1 AND status = 1"));
    }

    private async Task<Scenario> StartAsync(P0205Tests.CaptureDelivery? capture = null, RSA? deliveryKey = null)
    {
        await database.PrepareMerchantAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            "TRUNCATE public.rate_limit_buckets");
        var fixture = await P0201Fixture.CreateAsync(database);
        fixture.Clock.Now = P0202Fixture.WholeSecondNow();
        var settings = deliveryKey is null ? fixture.LicensingSettings() : P0205Tests.LicensingSettings(fixture, deliveryKey);
        settings["LicensingDatabase:MaxPoolSize"] = "4";
        var licensing = P0202Fixture.LicensingApi(fixture, settings: settings);
        var merchant = fixture.MerchantApi(
            handler: () => capture?.Attach(licensing.Server.CreateHandler()) ?? (HttpMessageHandler)licensing.Server.CreateHandler(),
            configure: services => services.AddSingleton<TimeProvider>(fixture.Clock),
            settings: deliveryKey is null ? null : P0205Tests.MerchantSettings(deliveryKey));
        var scenario = new Scenario(database, fixture, licensing, merchant);
        try
        {
            await scenario.ArrangeAsync();
            return scenario;
        }
        catch
        {
            await scenario.DisposeAsync();
            throw;
        }
    }

    internal static async Task<Guid> SeedShopAsync(LicensingPostgreSqlFixture database, Guid actor)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase).ConnectionString)
            .UseSnakeCaseNamingConvention().Options);
        var shop = Shop.Create("Removal fixture shop", "Fixture Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        db.Shops.Add(shop);
        db.ShopMemberships.Add(ShopMembership.Create(shop.Id, actor, ShopRole.Owner, isDefault: true));
        await db.SaveChangesAsync();
        return shop.Id;
    }

    internal static async Task<string> ShopAuthoritiesAsync(LicensingPostgreSqlFixture database) =>
        (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            """
            SELECT string_agg(id || ':' || shop_id || ':' || user_id || ':' || role || ':' || status || ':' || authority_version, ',' ORDER BY id)
            FROM public.shop_memberships
            """))!;

    private static Task<JsonElement> ReadAsync(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();

    private static Task AssertDeniedAsync(HttpResponseMessage response) =>
        AssertCodeAsync(response, HttpStatusCode.Forbidden, "Billing.AccountAccessDenied");

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, (await ReadAsync(response)).GetProperty("code").GetString());
        }
    }

    internal static async Task<Guid> CreateAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/billing/accounts",
            new { operationKey = Guid.NewGuid(), name = "Removal fixture account" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("accountId").GetGuid();
    }

    internal sealed class Scenario(LicensingPostgreSqlFixture database, P0201Fixture fixture,
        LicensingApiFactory licensing, MerchantStartupProbeFactory merchant) : IAsyncDisposable
    {
        private readonly Guid _key = Guid.NewGuid();
        public HttpClient Owner { get; } = merchant.CreateClient();
        public HttpClient Manager { get; } = merchant.CreateClient();
        public string Path { get; private set; } = "";
        public Guid Account => Guid.Parse(Path.Split('/')[^1]);
        public Guid OwnerActor { get; private set; }
        public Guid ManagerActor { get; private set; }
        public P0201Fixture Fixture => fixture;
        public LicensingApiFactory Licensing => licensing;
        public Guid Member { get; private set; }
        public string OwnerEmail { get; } = P0202Fixture.UniqueEmail();
        public string ManagerEmail { get; } = P0202Fixture.UniqueEmail();
        public Guid OwnerMember { get; private set; }
        public Guid Shop { get; private set; }
        public string ShopAuthorities { get; private set; } = "";

        public async Task ArrangeAsync()
        {
            OwnerActor = await P0201Tests.SeedMerchantAsync(merchant.Services, OwnerEmail);
            ManagerActor = await P0201Tests.SeedMerchantAsync(merchant.Services, ManagerEmail);
            Shop = await SeedShopAsync(database, ManagerActor);
            ShopAuthorities = await ShopAuthoritiesAsync(database);
            Owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await P0201Tests.LoginAsync(Owner, OwnerEmail));
            Manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await P0201Tests.LoginAsync(Manager, ManagerEmail));
            Path = $"/api/billing/accounts/{await CreateAsync(Owner):D}";
            using var issue = await Owner.PostAsJsonAsync(Path + "/invitations", new
            {
                operationKey = Guid.NewGuid(), expectedOwnerRevision = 1, recipientKind = "email", recipient = ManagerEmail,
            });
            Assert.Equal(HttpStatusCode.Accepted, issue.StatusCode);
            var inbox = await Manager.GetFromJsonAsync<JsonElement>("/api/billing/invitations");
            var invitation = Assert.Single(inbox.GetProperty("invitations").EnumerateArray()).GetProperty("invitationId").GetGuid();
            using var accepted = await Manager.PostAsJsonAsync($"/api/billing/invitations/{invitation:D}/accept",
                new { expectedInvitationRevision = 1 });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var roster = await Owner.GetFromJsonAsync<JsonElement>(Path + "/members");
            var members = roster.GetProperty("members").EnumerateArray().ToArray();
            Member = Assert.Single(members, row => row.GetProperty("role").GetString() == "Manager").GetProperty("memberId").GetGuid();
            OwnerMember = Assert.Single(members, row => row.GetProperty("isCurrent").GetBoolean()).GetProperty("memberId").GetGuid();
        }

        public Task<HttpResponseMessage> RemoveAsync(Guid? member = null, int version = 1, Guid? key = null,
            string reason = "Delegation ended", HttpClient? caller = null) =>
            (caller ?? Owner).PostAsJsonAsync($"{Path}/members/{member ?? Member:D}/remove",
                new { operationKey = key ?? _key, expectedVersion = version, reason });

        public Task<long> CountAsync(string sql) => P0202Fixture.CountAsync(database, fixture, sql);

        public async ValueTask DisposeAsync()
        {
            Owner.Dispose();
            Manager.Dispose();
            await merchant.DisposeAsync();
            await licensing.DisposeAsync();
            fixture.Dispose();
        }
    }
}
