using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The seat decision is made at its own instant and under the caller's live authority, not the
/// state observed when the request arrived and then queued behind the authority locks.
/// </summary>
public sealed partial class P0411LicensingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvitationExpiringWhileQueuedIsNotAccepted(bool full)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        // The operating grant outlasts the invitation by weeks; only the offer's deadline passes.
        var merchant = await MerchantAsync(client, allowance: 3, active: full ? 2 : 1);
        var invitee = await EnrollAsync(client, "expiring");
        var offer = await InviteAsync(host, client, merchant, invitee.Email);
        var expiresAt = _clock.Now.AddMinutes(1);
        await ExpireInvitationAtAsync(offer.Id, expiresAt);

        HttpResponseMessage response;
        await using (var holder = await HoldShopRowAsync(merchant.Shop))
        {
            var pending = AcceptAsync(client, invitee, offer);
            await WaitForShopWaitersAsync(holder, 1);
            _clock.Now = expiresAt;
            await holder.ReleaseAsync();
            response = await pending;
        }

        var body = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "Users.InvitationNotAcceptable");
        AssertNoCapacityDetail(body);
        Assert.Null(await MembershipAsync(merchant.Shop, invitee.UserId));
        Assert.Null(await AcceptedAtAsync(offer.Id));
        Assert.Equal(full ? 3 : 2, await CountActiveAsync(merchant.Shop));
    }

    [Fact]
    public async Task AcceptanceFromASessionRevokedWhileQueuedIsRefused()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var merchant = await MerchantAsync(client, allowance: 3, active: 1);
        var invitee = await EnrollAsync(client, "revoked");
        var offer = await InviteAsync(host, client, merchant, invitee.Email);

        var response = await WhileRevokingAsync(invitee, () => AcceptAsync(client, invitee, offer));

        AssertNoCapacityDetail(await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Auth.SessionInvalidated"));
        Assert.Null(await MembershipAsync(merchant.Shop, invitee.UserId));
        Assert.Null(await AcceptedAtAsync(offer.Id));
        Assert.Equal(2, await CountActiveAsync(merchant.Shop));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReinstatementFromAnOwnerSessionRevokedWhileQueuedIsRefused(bool full)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var merchant = await MerchantAsync(client, allowance: 3, active: full ? 2 : 1);
        var suspended = await PersonAsync(client, merchant.Shop, MembershipStatus.Suspended);
        var pendingOffer = await InviteAsync(host, client, merchant, suspended.Email);

        var response = await WhileRevokingAsync(merchant.Owner,
            () => PatchAsync(client, merchant, suspended.UserId, "Active", 1));

        // A revoked owner learns nothing about seat usage, even at a full shop.
        AssertNoCapacityDetail(await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Auth.SessionInvalidated"));
        var unchanged = await MembershipAsync(merchant.Shop, suspended.UserId);
        Assert.Equal((MembershipStatus.Suspended, 1L), (unchanged!.Status, unchanged.AuthorityVersion));
        Assert.Null(await RevokedAtAsync(pendingOffer.Id));
        Assert.Equal(full ? 3 : 2, await CountActiveAsync(merchant.Shop));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptanceFromASessionExpiringWhileQueuedIsRefused(bool full)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        // The grant and the offer outlast the device session; only the session's deadline passes.
        var merchant = await MerchantAsync(client, allowance: 3, active: full ? 2 : 1);
        var invitee = await EnrollAsync(client, "session-expiring");
        var offer = await InviteAsync(host, client, merchant, invitee.Email);

        var response = await WhileSessionExpiresAsync(merchant.Shop, invitee, () => AcceptAsync(client, invitee, offer));

        AssertNoCapacityDetail(await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Auth.SessionInvalidated"));
        Assert.Null(await MembershipAsync(merchant.Shop, invitee.UserId));
        Assert.Null(await AcceptedAtAsync(offer.Id));
        Assert.Null(await RevokedAtAsync(offer.Id));
        Assert.Equal(full ? 3 : 2, await CountActiveAsync(merchant.Shop));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReinstatementFromAnOwnerSessionExpiringWhileQueuedIsRefused(bool full)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var merchant = await MerchantAsync(client, allowance: 3, active: full ? 2 : 1);
        var suspended = await PersonAsync(client, merchant.Shop, MembershipStatus.Suspended);
        var pendingOffer = await InviteAsync(host, client, merchant, suspended.Email);

        var response = await WhileSessionExpiresAsync(merchant.Shop, merchant.Owner,
            () => PatchAsync(client, merchant, suspended.UserId, "Active", 1));

        // An expired owner session learns nothing about seat usage, even at a full shop.
        AssertNoCapacityDetail(await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Auth.SessionInvalidated"));
        var unchanged = await MembershipAsync(merchant.Shop, suspended.UserId);
        Assert.Equal((MembershipStatus.Suspended, 1L), (unchanged!.Status, unchanged.AuthorityVersion));
        Assert.Null(await RevokedAtAsync(pendingOffer.Id));
        Assert.Equal(full ? 3 : 2, await CountActiveAsync(merchant.Shop));
    }

    /// <summary>
    /// Sets the actor's device session to lapse a minute from now, queues the request on the shop
    /// root with its session already checked, then moves the clock to that deadline before release.
    /// </summary>
    private async Task<HttpResponseMessage> WhileSessionExpiresAsync(
        Guid shop, Person actor, Func<Task<HttpResponseMessage>> send)
    {
        var expiresAt = _clock.Now.AddMinutes(1);
        await ExpireSessionAtAsync(actor, expiresAt);
        await using var holder = await HoldShopRowAsync(shop);
        var pending = send();
        await WaitForShopWaitersAsync(holder, 1);
        _clock.Now = expiresAt;
        await holder.ReleaseAsync();
        return await pending;
    }

    private async Task ExpireSessionAtAsync(Person person, DateTimeOffset expiresAt)
    {
        var session = Guid.Parse(new JwtSecurityToken(person.Token).Claims.Single(x => x.Type == "sid").Value);
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync(
            $"UPDATE auth_sessions SET expires_at = {expiresAt} WHERE id = {session}", Ct));
    }

    private async Task<HttpResponseMessage> WhileRevokingAsync(Person actor, Func<Task<HttpResponseMessage>> send)
    {
        await using var hold = await HoldAndRevokeSessionAsync(actor);
        var pending = send();
        await WaitForBlockedByAsync(pending, hold);
        await hold.ReleaseAsync();
        return await pending;
    }

    private async Task ExpireInvitationAtAsync(Guid invitation, DateTimeOffset expiresAt)
    {
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync(
            $"UPDATE shop_invitations SET expires_at = {expiresAt} WHERE id = {invitation}", Ct));
    }

    private async Task<DateTimeOffset?> RevokedAtAsync(Guid invitation)
    {
        await using var db = Open(Migrator);
        return await db.Set<ShopInvitation>().Where(x => x.Id == invitation).Select(x => x.RevokedAt).SingleAsync(Ct);
    }

    private static void AssertNoCapacityDetail(JsonElement body) =>
        Assert.False(body.TryGetProperty("memberCapacity", out _), $"Unexpected capacity detail: {body}");
}
