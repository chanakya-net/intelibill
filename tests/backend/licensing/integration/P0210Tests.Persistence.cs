using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0210Tests
{
    [Fact]
    public async Task TrialStatusExpiresAtFourteenDaysWithoutGraceOrCommerceGate()
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        await fixture.LinkAsync(account, owner.UserId, owner);
        var result = await StartAndWaitAsync(owner, fixture.Accept());
        Assert.Equal("Applied", result.Status);

        var active = await AssertStatusAsync(owner, result, "Active");
        await P0209Tests.PostStockAsync(owner.Client, "Trial stock before expiry");
        fixture.Hosts.StatusClock.Now = result.EndsAt.AddTicks(-10);
        await AssertStatusAsync(owner, result, "Active");
        fixture.Hosts.StatusClock.Now = result.EndsAt;
        var expired = await AssertStatusAsync(owner, result, "Expired");
        Assert.Equal(active.GetProperty("termId").GetGuid(), expired.GetProperty("termId").GetGuid());
        await P0209Tests.PostStockAsync(owner.Client, "Trial stock at exact expiry");
        fixture.Hosts.StatusClock.Now = result.EndsAt.AddTicks(10);
        await AssertStatusAsync(owner, result, "Expired");

        var page = await ReadAsync(await owner.Client.GetAsync(new Uri($"/api/billing/accounts/{account:D}/subscriptions", UriKind.Relative)));
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(owner.ShopId, Assert.Single(item.GetProperty("coveredShops").EnumerateArray()).GetProperty("shopId").GetGuid());
        Assert.True(JsonElement.DeepEquals(item.GetProperty("status"), expired));
        Assert.Equal(0L, await fixture.CountAsync("subscription_term_facts"));
        await AssertRetainedAsync(fixture);
        await AssertTrialTermReservedAsync(fixture, owner, account, result, active.GetProperty("termId").GetGuid());
    }

    [Fact]
    public async Task InvalidPolicyAndMemberCapRefuseWithoutGranting()
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        await fixture.LinkAsync(account, owner.UserId, owner);
        var stale = fixture.Accept();
        Assert.Equal("PolicyChanged", (await StartAndWaitAsync(owner, stale with { PolicyRevision = stale.PolicyRevision + 1 })).Reason);
        for (var index = 0; index < 5; index++)
            await SignInAsync(fixture.Hosts, ShopRole.Staff, owner.ShopId);
        var preview = await owner.Client.GetFromJsonAsync<ShopTrialPreviewV1>(Path(owner.ShopId));
        Assert.Equal(6, preview!.ActiveMembers);
        Assert.Equal("MemberCap", preview.Reason);
        Assert.Equal("MemberCap", (await StartAndWaitAsync(owner, fixture.Accept())).Reason);
        Assert.Equal(0L, await fixture.CountAsync("trial_history"));
        Assert.Equal(0L, await fixture.CountAsync("subscription_coverage_terms"));
    }

    [Fact]
    public async Task InternalTrialRequiresItsBoundServiceAssertion()
    {
        await using var fixture = await P0210Fixture.StartAsync(database, publish: false);
        using var api = fixture.Hosts.Licensing.CreateClient();
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        using var signed = fixture.Delivery(Consent(fixture, owner));
        signed.Content!.Dispose();
        signed.Content = JsonContent.Create(Consent(fixture, owner));
        using var refused = await api.SendAsync(signed);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(0L, await fixture.CountAsync("shop_trial_delivery_uses"));
    }

    private static async Task<JsonElement> AssertStatusAsync(Merchant shop, ShopTrialOperationV1 result, string state)
    {
        var status = await ReadAsync(await ShopStatusAsync(shop.Client, shop.ShopId));
        Assert.Equal(state, status.GetProperty("state").GetString());
        Assert.True(status.GetProperty("observeOnly").GetBoolean());
        var term = status.GetProperty("term");
        Assert.Equal("Trial", term.GetProperty("basis").GetString());
        Assert.Equal(result.EndsAt, term.GetProperty("accessEndsAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, term.GetProperty("graceEndsAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, term.GetProperty("billingPeriod").ValueKind);
        Assert.Equal(JsonValueKind.Null, term.GetProperty("commercial").ValueKind);
        Assert.Equal(JsonValueKind.Null, term.GetProperty("nextPeriodEndsAt").ValueKind);
        return term;
    }

    private static async Task AssertTrialTermReservedAsync(P0210Fixture fixture, Merchant owner, Guid account,
        ShopTrialOperationV1 result, Guid term)
    {
        var listing = await ReadAsync(await owner.Client.GetAsync(new Uri($"/api/billing/accounts/{account:D}/affiliations", UriKind.Relative)));
        Assert.Empty(listing.GetProperty("terms").EnumerateArray());
        var other = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => AffiliateAsync(fixture.Hosts,
            account, owner.UserId, other.ShopId, other.UserId, term, result.AcceptedAt, result.EndsAt));
        Assert.Equal("23514", rejected.SqlState);
    }

    private async Task AssertRetainedAsync(P0210Fixture fixture)
    {
        var runtime = database.Login(fixture.LicensingOwner.Database!, LicensingPostgreSqlFixture.RuntimeRole);
        Assert.Equal(0L, (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(runtime, "SELECT count(*) FROM trial_history"))!);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(
            fixture.LicensingOwner, "DELETE FROM trial_history"));
        Assert.Equal("55000", denied.SqlState);
    }
}
