using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0209Tests
{
    [Fact]
    public async Task SignedMerchantStatusSeparatesShopAndBillingAuthority()
    {
        await using var host = await StartAsync(database, Utc("2027-02-10T00:00:00Z"));
        var owner = await SignInAsync(host, ShopRole.Owner);
        var staff = await SignInAsync(host, ShopRole.Staff, owner.ShopId);
        var payingStaff = await SignInAsync(host, ShopRole.Staff, owner.ShopId);
        var billingOnly = await SignInAsync(host);
        var account = await CreateAccountAsync(payingStaff.Client);
        var term = await SeedTermAsync(host, account, new TermSeed(Jan31, Feb28, AnchorLocal: "2027-01-31 10:00:00"));
        await AffiliateAsync(host, account, payingStaff.UserId, owner.ShopId, owner.UserId, term, Jan31, Feb28);

        // Staff without billing membership: the same operational status, no price, no account identity.
        var operational = await ReadAsync(await ShopStatusAsync(staff.Client, owner.ShopId));
        Assert.Equal("Active", operational.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, operational.GetProperty("accountId").ValueKind);
        Assert.Equal(JsonValueKind.Null, operational.GetProperty("term").GetProperty("commercial").ValueKind);
        Assert.DoesNotContain("149900", operational.GetRawText());

        // Staff who is also the Billing Owner: a shop Staff role never suppresses the commercial subview.
        var commercial = await ReadAsync(await ShopStatusAsync(payingStaff.Client, owner.ShopId));
        Assert.Equal(account, commercial.GetProperty("accountId").GetGuid());
        Assert.Equal(149900, commercial.GetProperty("term").GetProperty("commercial").GetProperty("pricePaise").GetInt64());
        Assert.Equal(Without(operational, "accountId", "commercial"), Without(commercial, "accountId", "commercial"));

        // A billing-only member with no shop: account status, never shop status or shop rights.
        using var notMember = await billingOnly.Client.GetAsync($"/api/billing/accounts/{account:D}/subscriptions");
        Assert.Equal(HttpStatusCode.Forbidden, notMember.StatusCode);
        var page = await ReadAsync(await payingStaff.Client.GetAsync($"/api/billing/accounts/{account:D}/subscriptions"));
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(owner.ShopId, Assert.Single(item.GetProperty("coveredShops").EnumerateArray()).GetProperty("shopId").GetGuid());
        Assert.Equal(Feb28, item.GetProperty("status").GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(149900, item.GetProperty("status").GetProperty("commercial").GetProperty("pricePaise").GetInt64());
        using var foreignShop = await ShopStatusAsync(billingOnly.Client, owner.ShopId);
        Assert.Equal(HttpStatusCode.Forbidden, foreignShop.StatusCode);
        using var items = await billingOnly.Client.GetAsync("/api/items");
        Assert.NotEqual(HttpStatusCode.OK, items.StatusCode);
    }

    [Fact]
    public async Task AccountStatusPagesEveryTermInStableOrder()
    {
        await using var host = await StartAsync(database, Utc("2027-02-10T00:00:00Z"));
        var payer = await SignInAsync(host);
        var account = await CreateAccountAsync(payer.Client);
        for (var index = 0; index < 51; index++)
            await SeedTermAsync(host, account, new TermSeed(Jan31, Feb28, Basis: index % 2 == 0 ? null : "Paid",
                AnchorLocal: "2027-01-31 10:00:00"));

        var first = await ReadAsync(await payer.Client.GetAsync($"/api/billing/accounts/{account:D}/subscriptions"));
        Assert.Equal(50, first.GetProperty("items").GetArrayLength());
        var cursor = first.GetProperty("nextCursor").GetGuid();
        var second = await ReadAsync(await payer.Client.GetAsync($"/api/billing/accounts/{account:D}/subscriptions?after={cursor:D}"));
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var ids = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray())
            .Select(row => row.GetProperty("status").GetProperty("termId").GetGuid()).ToArray();
        Assert.Equal(51, ids.Distinct().Count());
        Assert.Equal(ids.Order(), ids);
    }

    [Fact]
    public async Task CorrectlySignedButRebindedStatusAssertionsAreRefused()
    {
        await using var host = await StartAsync(database, Utc("2027-02-10T00:00:00Z"));
        using var licensing = host.Licensing.CreateClient();
        var actor = Guid.NewGuid();
        var shop = Guid.NewGuid();
        var other = Guid.NewGuid();

        // Signed for one shop, presented on another shop's path.
        var signed = P0202Fixture.Sign(host.Fixture, actor, "licensing.shop-status.read", [],
            claims => claims["path"] = $"/internal/licensing/v1/customer/shops/{shop:D}/status");
        using var moved = await P0202Fixture.SendAsync(licensing, signed, path: $"/internal/licensing/v1/customer/shops/{other:D}/status");
        Assert.Equal(HttpStatusCode.Unauthorized, moved.StatusCode);
        // Signed for a different action on the right path.
        var wrongAction = P0202Fixture.Sign(host.Fixture, actor, "billing.account.list", [],
            claims => claims["path"] = $"/internal/licensing/v1/customer/shops/{shop:D}/status");
        using var refused = await P0202Fixture.SendAsync(licensing, wrongAction, path: $"/internal/licensing/v1/customer/shops/{shop:D}/status");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        // An exact binding succeeds and answers PendingActivation for an unknown shop; no data is invented.
        var exact = P0202Fixture.Sign(host.Fixture, actor, "licensing.shop-status.read", [],
            claims => claims["path"] = $"/internal/licensing/v1/customer/shops/{shop:D}/status");
        var pending = await ReadAsync(await P0202Fixture.SendAsync(licensing, exact, path: $"/internal/licensing/v1/customer/shops/{shop:D}/status"));
        Assert.Equal("PendingActivation", pending.GetProperty("state").GetString());
        // Delegation tokens never authenticate provider routes.
        var provider = P0202Fixture.Sign(host.Fixture, actor, "licensing.shop-status.read", [],
            claims => claims["path"] = $"/api/admin/accounts/{other:D}/status");
        using var admin = await P0202Fixture.SendAsync(licensing, provider, path: $"/api/admin/accounts/{other:D}/status");
        Assert.Equal(HttpStatusCode.Unauthorized, admin.StatusCode);
    }

    [Fact]
    public async Task ShopStatusDiscardsTheReplyWhenMembershipChangesInFlight()
    {
        var transport = new AfterShopReadTransport();
        await using var host = await StartAsync(database, Utc("2027-02-10T00:00:00Z"), inner =>
        {
            transport.InnerHandler = inner;
            return transport;
        });
        var payer = await SignInAsync(host);
        var account = await CreateAccountAsync(payer.Client);
        var owner = await SignInAsync(host, ShopRole.Owner);
        var staff = await SignInAsync(host, ShopRole.Staff, owner.ShopId);
        var term = await SeedTermAsync(host, account, new TermSeed(Jan31, Feb28, AnchorLocal: "2027-01-31 10:00:00"));
        await AffiliateAsync(host, account, payer.UserId, owner.ShopId, owner.UserId, term, Jan31, Feb28);

        transport.Path = SubscriptionStatusV1.ShopPath(owner.ShopId);
        transport.AfterRead = async () =>
        {
            await using var db = MerchantDb(database);
            var membership = await db.ShopMemberships.SingleAsync(row => row.ShopId == owner.ShopId && row.UserId == staff.UserId);
            membership.ChangeAuthority(ShopRole.Staff, MembershipStatus.Suspended);
            await db.SaveChangesAsync();
        };

        using var response = await ShopStatusAsync(staff.Client, owner.ShopId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(transport.CompletedSuccessfulRead);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Licensing.ShopScopeRequired", JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("paidThrough", body);
    }

    private sealed class AfterShopReadTransport : DelegatingHandler
    {
        internal string? Path { get; set; }
        internal Func<Task>? AfterRead { get; set; }
        internal bool CompletedSuccessfulRead { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (request.RequestUri?.AbsolutePath != Path || AfterRead is not { } after || !response.IsSuccessStatusCode)
                return response;
            try
            {
                CompletedSuccessfulRead = true;
                await after();
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
    }

    private static string Without(JsonElement status, params string[] fields)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(status.GetRawText())!.AsObject();
        foreach (var field in fields) { node.Remove(field); node["term"]?.AsObject().Remove(field); }
        return node.ToJsonString();
    }
}
