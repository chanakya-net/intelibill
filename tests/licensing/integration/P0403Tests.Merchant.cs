using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0403Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvitationUsesTargetAccountWithOppositeOrAbsentActiveShop(bool withOppositeShop)
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var restricted = await CreateTradingShopAsync(fixture.Hosts);
        var permitted = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, restricted);
        await LinkAsync(fixture, permitted);
        await fixture.DrainAsync(source: true);
        await EnrollAsync(fixture, restricted, permitted);
        using var admin = await SignInSecurityAsync(fixture);
        await ApplySuspensionAsync(admin, account, "Account", null, "Security");
        await fixture.DrainAsync(source: true);

        var actor = withOppositeShop
            ? await SignInAsync(fixture.Hosts, ShopRole.Staff, permitted.ShopId)
            : await SignInAsync(fixture.Hosts);
        var blockedOffer = await SeedInvitationAsync(fixture, restricted, actor);
        var allowedOffer = await SeedInvitationAsync(fixture, permitted, actor);
        using var blocked = await AcceptInvitationAsync(actor, blockedOffer);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("Licence.SecuritySuspended", await P0206Fixture.CodeAsync(blocked));

        if (withOppositeShop)
        {
            // Add an existing membership in the restricted shop so the same identity can choose it.
            await using var db = MerchantDb(database);
            db.ShopMemberships.Add(ShopMembership.Create(restricted.ShopId, actor.UserId, ShopRole.Staff, false));
            await db.SaveChangesAsync();
            using var switched = await actor.Client.PostAsJsonAsync("/api/shops/switch", new { shopId = restricted.ShopId });
            Assert.True(switched.StatusCode == HttpStatusCode.OK, await switched.Content.ReadAsStringAsync());
            var token = (await switched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
            actor.Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        }

        using var accepted = await AcceptInvitationAsync(actor, allowedOffer);
        Assert.True(accepted.StatusCode == HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        Assert.Equal(permitted.ShopId, (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("shopId").GetGuid());
        await using var verify = MerchantDb(database);
        Assert.Null((await verify.Set<ShopInvitation>().SingleAsync(row => row.Id == blockedOffer.Id)).AcceptedAt);
        Assert.NotNull((await verify.Set<ShopInvitation>().SingleAsync(row => row.Id == allowedOffer.Id)).AcceptedAt);
    }

    [Fact]
    public async Task TradingSuspensionSeparatesStatusOnlyRemovalFromRoleExpansion()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, shop);
        var member = await SignInAsync(fixture.Hosts, ShopRole.Staff, shop.ShopId);
        await fixture.DrainAsync(source: true);
        await EnrollAsync(fixture, shop);
        using var admin = await SignInSecurityAsync(fixture);
        await ApplySuspensionAsync(admin, account, "Account", null, "Trading");
        await fixture.DrainAsync(source: true);
        await using var db = MerchantDb(database);
        var version = await db.ShopMemberships.Where(row => row.ShopId == shop.ShopId && row.UserId == member.UserId)
            .Select(row => row.AuthorityVersion).SingleAsync();
        var path = $"/api/shops/{shop.ShopId:D}/members/{member.UserId:D}";

        using var expansion = await shop.Client.PatchAsJsonAsync(path, new { role = "Manager", status = "Active", expectedVersion = version });
        Assert.Equal(HttpStatusCode.Forbidden, expansion.StatusCode);
        Assert.Equal("Licence.TradingRestricted", await P0206Fixture.CodeAsync(expansion));
        using var removal = await shop.Client.PatchAsJsonAsync(path, new { role = "Staff", status = "Removed", expectedVersion = version });
        Assert.True(removal.StatusCode == HttpStatusCode.OK, await removal.Content.ReadAsStringAsync());
        var actual = await db.ShopMemberships.SingleAsync(row => row.ShopId == shop.ShopId && row.UserId == member.UserId);
        Assert.Equal(MembershipStatus.Removed, actual.Status);
        Assert.Equal(ShopRole.Staff, actual.Role);
        Assert.Equal(version + 1, actual.AuthorityVersion);
    }

    [Fact]
    public async Task CustomerStatusReportsEnforcementOnlyInTheEnrolledShopAtTheEffectiveInstant()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var enrolled = await CreateTradingShopAsync(fixture.Hosts);
        var observeOnly = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, enrolled, observeOnly);
        await fixture.DrainAsync(source: true);
        await EnrollAsync(fixture, enrolled);
        using var admin = await SignInSecurityAsync(fixture);
        var effective = fixture.Hosts.Fixture.Clock.Now.AddSeconds(10);
        await ApplySuspensionAsync(admin, account, "Account", null, "Security", effective);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);

        using (var before = await ShopStatusAsync(enrolled.Client, enrolled.ShopId))
        {
            var status = await before.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(status.TryGetProperty("enforcedSuspensionMode", out _));
        }

        fixture.Hosts.Fixture.Clock.Now = effective;
        await AssertCustomerEnforcementAsync(enrolled, false);
        await AssertCustomerEnforcementAsync(observeOnly, true);
        await AssertLegacySecurityAsync(enrolled);
        await AssertReadableAsync(observeOnly);
    }

    private static async Task AssertCustomerEnforcementAsync(Merchant shop, bool observeOnly)
    {
        using var response = await ShopStatusAsync(shop.Client, shop.ShopId);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var status = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(observeOnly, status.GetProperty("observeOnly").GetBoolean());
        Assert.Equal("Applied", status.GetProperty("entitlementSynchronization").GetString());
        Assert.Equal("Security", status.GetProperty("suspension").GetProperty("mode").GetString());
        var enforced = status.TryGetProperty("enforcedSuspensionMode", out var mode);
        Assert.Equal(!observeOnly, enforced);
        if (enforced) Assert.Equal("Security", mode.GetString());
    }

    private static async Task<(Guid Id, string Token)> SeedInvitationAsync(P0213Fixture fixture, Merchant shop, Merchant actor)
    {
        await using var db = MerchantDb(fixture.Hosts.Database);
        var membership = await db.ShopMemberships.SingleAsync(row => row.ShopId == shop.ShopId && row.UserId == shop.UserId);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var offer = ShopInvitation.Create(shop.ShopId, actor.Email, ShopRole.Staff, hash, shop.UserId,
            membership.AuthorityVersion, fixture.Hosts.Fixture.Clock.Now.AddDays(1));
        db.Add(offer);
        await db.SaveChangesAsync();
        return (offer.Id, token);
    }

    private static Task<HttpResponseMessage> AcceptInvitationAsync(Merchant actor, (Guid Id, string Token) offer) =>
        actor.Client.PostAsJsonAsync($"/api/invitations/{offer.Id:D}/accept", new { token = offer.Token });
}
