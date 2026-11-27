using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0213Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0214Tests
{
    [Fact]
    public async Task ConcurrentAcknowledgementsAndReadsStayCoherent()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var (owner, account) = await LinkAndConfirmAsync(fixture);
        var manager = fixture.Trial.CatalogueManager!;
        await SaveGrantAsync(fixture, manager, account, owner.ShopId);
        var snapshot = await fixture.LatestSnapshotAsync(owner.ShopId);
        var ack = await SendReceiptAsync(fixture, snapshot);
        var writes = Enumerable.Range(0, 3).Select(_ => SendAcknowledgementAsync(fixture, ack));
        fixture.Hosts.StatusClock.Now += TimeSpan.FromHours(47);
        var reads = Task.WhenAll(Enumerable.Range(0, 6).Select(_ => MerchantStatusAsync(owner)));
        await Task.WhenAll(writes.Append(fixture.ScanAsync(source: true)).Append(reads));

        foreach (var status in await reads)
        {
            var sync = status.GetProperty("synchronization");
            var applied = sync.GetProperty("appliedAccountRevision").GetInt64() >= sync.GetProperty("savedAccountRevision").GetInt64()
                && sync.GetProperty("appliedShopRevision").GetInt64() >= sync.GetProperty("savedShopRevision").GetInt64();
            Assert.Equal(applied, sync.GetProperty("state").GetString() == "Applied");
        }
        Assert.Equal(1L, await fixture.SourceScalarAsync(
            $"SELECT count(*) FROM entitlement_merchant_inbox WHERE event_id = '{snapshot.EventId:D}'"));
        await BothAsync(owner, manager, account, "Pending", "Queued");
    }

    [Fact]
    public async Task SynchronizationIsScopedAndRedacted()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var (owner, account) = await LinkAndConfirmAsync(fixture);
        var (other, foreignAccount) = await LinkAndConfirmAsync(fixture);
        var manager = fixture.Trial.CatalogueManager!;
        await SaveGrantAsync(fixture, manager, account, owner.ShopId);
        var staff = await SignInAsync(fixture.Hosts, ShopRole.Staff, owner.ShopId);
        var own = await BothAsync(owner, manager, account, "Pending", "Queued");
        var staffView = await MerchantStatusAsync(staff);
        Assert.True(JsonElement.DeepEquals(own, staffView.GetProperty("synchronization")));
        Assert.Equal(JsonValueKind.Null, staffView.GetProperty("accountId").ValueKind);
        Assert.Equal(JsonValueKind.Null, staffView.GetProperty("term").GetProperty("commercial").ValueKind);
        Assert.DoesNotContain("Launch partner", staffView.GetRawText());
        await BothAsync(other, manager, foreignAccount, "Applied", "Acknowledged");
        await AssertProviderDeniedAsync(fixture, manager, foreignAccount, owner.ShopId, account);

        using var page = await owner.Client.GetAsync($"/api/billing/accounts/{account:D}/subscriptions");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var body = await page.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", body.GetProperty("entitlementSynchronization").GetString());
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        var shop = Assert.Single(item.GetProperty("coveredShops").EnumerateArray());
        Assert.True(JsonElement.DeepEquals(own, shop.GetProperty("synchronization")));
    }

    private static async Task AssertProviderDeniedAsync(P0213Fixture fixture, P0110CookieBrowser manager,
        Guid foreignAccount, Guid shop, Guid account)
    {
        using var foreign = await manager.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{foreignAccount:D}/shops/{shop:D}/status");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var path = $"/api/admin/accounts/{account:D}/shops/{shop:D}/status";
        using var ownerOnly = await fixture.Trial.Catalogue.SignInOwnerAsync();
        using var denied = await ownerOnly.SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var body = await denied.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Status.AccessDenied", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("synchronization", out _));
        using var anonymous = await fixture.LicensingApi.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task DisabledTransportKeepsSavedSnapshotsUnavailable()
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        await fixture.LinkAsync(account, owner.UserId, owner);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(owner, fixture.Accept())).Status);
        Assert.Equal(1L, await fixture.CountAsync("entitlement_snapshots"));

        var merchant = await MerchantStatusAsync(owner);
        var provider = await P0212Fixture.ShopStatusAsync(fixture.CatalogueManager!, account, owner.ShopId);
        foreach (var status in new[] { merchant, provider })
        {
            Assert.Equal("NotAvailable", status.GetProperty("entitlementSynchronization").GetString());
            Assert.Equal(JsonValueKind.Null, status.GetProperty("synchronization").ValueKind);
        }
    }

    [Theory]
    [InlineData("Applied")]
    [InlineData("Unknown")]
    public async Task MerchantRelayRejectsIncoherentConfirmation(string state)
    {
        var transport = new IncoherentStatusTransport(state);
        await using var hosts = await StartAsync(database, P0210Fixture.Start, inner => { transport.InnerHandler = inner; return transport; });
        var owner = await SignInAsync(hosts, ShopRole.Owner);
        transport.Path = SubscriptionStatusV1.ShopPath(owner.ShopId);
        using var response = await ShopStatusAsync(owner.Client, owner.ShopId);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Billing.DelegationUnavailable", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public void StatusContractCarriesSynchronizationWithinBudget()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/subscription-status-v1.json")));
        var contract = document.RootElement;
        var sync = contract.GetProperty("synchronizationExample").Deserialize<EntitlementSynchronizationV1>()!;
        Assert.Equal(SubscriptionStatusV1.SynchronizationStates, contract.GetProperty("synchronizationStates").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(SubscriptionStatusV1.DeliveryStates, contract.GetProperty("deliveryStates").EnumerateArray().Select(x => x.GetString()));
        Assert.True(JsonElement.DeepEquals(contract.GetProperty("synchronizationExample"), JsonSerializer.SerializeToElement(sync)));
        var legacy = contract.GetProperty("example").Deserialize<ShopSubscriptionStatusV1>()!;
        Assert.Null(legacy.Synchronization);

        var large = sync with { State = "NeedsReview", Delivery = "Acknowledged", SavedAccountRevision = long.MaxValue,
            SavedShopRevision = long.MaxValue, AppliedAccountRevision = long.MaxValue - 10, AppliedShopRevision = long.MaxValue - 10,
            SavedVerifiedAt = P0210Fixture.Start, LastSynchronizedAt = P0210Fixture.Start, Attention = "InvalidAcknowledgement" };
        var covered = new CoveredShopV1(Guid.NewGuid(), new string('\u0915', 250), large);
        var item = new AccountTermStatusV1(legacy.Term!, Enumerable.Repeat(covered, SubscriptionStatusV1.MaximumCoveredShops).ToArray(),
            SubscriptionStatusV1.MaximumCoveredShops);
        var page = new AccountSubscriptionStatusV1(1, Guid.NewGuid(), P0210Fixture.Start,
            Enumerable.Repeat(item, SubscriptionStatusV1.PageSize).ToArray(), null, true, "NeedsReview", "NotAvailable");
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(page).Length, 1, SubscriptionStatusV1.AccountResponseLimit);
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(legacy with { Synchronization = large }).Length, 1, SubscriptionStatusV1.ShopResponseLimit);
    }

    private sealed class IncoherentStatusTransport(string state) : DelegatingHandler
    {
        internal string? Path { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (request.RequestUri?.AbsolutePath != Path || !response.IsSuccessStatusCode) return response;
            var status = (await response.Content.ReadFromJsonAsync<ShopSubscriptionStatusV1>(ct))!;
            response.Content.Dispose();
            response.Content = JsonContent.Create(status with
            {
                EntitlementSynchronization = state,
                Synchronization = new(state, "Acknowledged", 5, 8, P0210Fixture.Start, 4, 8, P0210Fixture.Start, null),
            });
            return response;
        }
    }
}
