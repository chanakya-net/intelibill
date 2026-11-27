using System.Net;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0411LicensingTests
{
    [Fact]
    public async Task AdmissionFollowsTheDecisionAtItsOwnInstant()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var merchant = await MerchantAsync(client, allowance: 4, active: 1, end: end);
        var early = await EnrollAsync(client, "early");
        var late = await EnrollAsync(client, "late");
        var earlyOffer = await InviteAsync(host, client, merchant, early.Email);
        var lateOffer = await InviteAsync(host, client, merchant, late.Email);

        _clock.Now = end.AddTicks(-1);
        await ReadSuccessAsync(await AcceptAsync(client, early, earlyOffer));
        _clock.Now = end;
        await AssertProblemAsync(await AcceptAsync(client, late, lateOffer), HttpStatusCode.Forbidden, "Licence.Expired");
        Assert.Null(await MembershipAsync(merchant.Shop, late.UserId));

        // Removal stays available after trading ends.
        await ReadSuccessAsync(await PatchAsync(client, merchant, early.UserId, "Removed", 0));
        Assert.Equal(2, await CountActiveAsync(merchant.Shop));
    }

    [Fact]
    public async Task AdmissionWaitingOnTheShopIsDecidedWhenItProceeds()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var merchant = await MerchantAsync(client, allowance: 4, active: 1, end: end);
        var invitee = await EnrollAsync(client, "waiting");
        var offer = await InviteAsync(host, client, merchant, invitee.Email);

        await using var holder = await HoldShopRowAsync(merchant.Shop);
        var pending = AcceptAsync(client, invitee, offer);
        await WaitForShopWaitersAsync(holder, 1);
        _clock.Now = end;
        await holder.ReleaseAsync();

        await AssertProblemAsync(await pending, HttpStatusCode.Forbidden, "Licence.Expired");
        Assert.Null(await MembershipAsync(merchant.Shop, invitee.UserId));
    }

    [Fact]
    public async Task PendingActivationAndUnknownAllowanceAdmitNobodyButKeepRemoval()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var pending = await MerchantAsync(client, allowance: 5, active: 1, state: "PendingActivation");
        var invitee = await EnrollAsync(client, "pending");
        var offer = await InviteAsync(host, client, pending, invitee.Email);
        await AssertProblemAsync(await AcceptAsync(client, invitee, offer), HttpStatusCode.Forbidden, "Licence.PendingActivation");
        await ReadSuccessAsync(await PatchAsync(client, pending, await FirstStaffAsync(pending.Shop), "Suspended", 0));

        var unknown = await MerchantAsync(client, allowance: 5, active: 1);
        await ApplyAsync(unknown.Binding, _clock.Now.AddDays(30), revision: 2, allowance: null);
        var second = await EnrollAsync(client, "unknown");
        var unknownOffer = await InviteAsync(host, client, unknown, second.Email);
        await AssertProblemAsync(await AcceptAsync(client, second, unknownOffer), HttpStatusCode.ServiceUnavailable,
            "Licence.VerificationUnavailable");
        await ReadSuccessAsync(await PatchAsync(client, unknown, await FirstStaffAsync(unknown.Shop), "Removed", 0));
        Assert.Null(await AcceptedAtAsync(unknownOffer.Id));
    }

    [Fact]
    public async Task ObserveOnlyAndUnprojectedShopsAreNotCapped()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var observed = await MerchantAsync(client, allowance: 1, active: 2, enforce: false);
        var invitee = await EnrollAsync(client, "observed");
        await ReadSuccessAsync(await AcceptAsync(client, invitee, await InviteAsync(host, client, observed, invitee.Email)));
        Assert.Equal(4, await CountActiveAsync(observed.Shop));

        var owner = await EnrollAsync(client, "unprojected");
        var (shop, token) = await CreateShopAsync(client, owner.Token);
        var unprojected = new Merchant(shop, owner with { Token = token }, new(shop, Guid.Empty, Guid.Empty, Guid.Empty));
        var member = await EnrollAsync(client, "unprojected-member");
        await ReadSuccessAsync(await AcceptAsync(client, member, await InviteAsync(host, client, unprojected, member.Email)));
        Assert.Equal(2, await CountActiveAsync(shop));
    }

    [Fact]
    public async Task ShopCreationSeatsOnlyItsInitialOwner()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await MerchantAsync(client, allowance: 3, active: 2);

        var (created, token) = await CreateShopAsync(client, source.Owner.Token);
        await using (var db = Open(Migrator))
        {
            var members = await db.ShopMemberships.AsNoTracking().Where(x => x.ShopId == created).ToListAsync(Ct);
            var seat = Assert.Single(members);
            Assert.Equal((source.Owner.UserId, ShopRole.Owner, MembershipStatus.Active), (seat.UserId, seat.Role, seat.Status));
            Assert.False(await db.Set<ShopLicenseState>().AnyAsync(x => x.ShopId == created, Ct));
        }
        Assert.Equal(3, await CountActiveAsync(source.Shop));

        // A first projection of one seat leaves the owner seated and admits nobody else.
        var merchant = new Merchant(created, source.Owner with { Token = token }, await BindAsync(created));
        await ApplyAsync(merchant.Binding, _clock.Now.AddDays(30), allowance: 1);
        await EnforceAsync(created);
        var invitee = await EnrollAsync(client, "second-shop");
        var offer = await InviteAsync(host, client, merchant, invitee.Email);
        await AssertProblemAsync(await AcceptAsync(client, invitee, offer), HttpStatusCode.Conflict, CapacityCode);
        Assert.Equal(1, await CountActiveAsync(created));
    }
}
