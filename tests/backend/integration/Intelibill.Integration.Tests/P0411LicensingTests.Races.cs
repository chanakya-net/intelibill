using System.Net;
using Intelibill.Domain.Enums;

namespace Intelibill.Integration.Tests;

public sealed partial class P0411LicensingTests
{
    [Fact]
    public async Task TwoFinalSeatAcceptancesAdmitExactlyOne()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var merchant = await MerchantAsync(client, allowance: 3, active: 1);
        var first = await EnrollAsync(client, "first");
        var second = await EnrollAsync(client, "second");
        var firstOffer = await InviteAsync(host, client, merchant, first.Email);
        var secondOffer = await InviteAsync(host, client, merchant, second.Email);

        // The first admission pauses after its INSERT with every lock held; the second must queue
        // behind it and then see its commit.
        HttpResponseMessage[] responses;
        await using (var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "shop_memberships", merchant.Shop))
        {
            var firstTask = AcceptAsync(client, first, firstOffer);
            var firstPid = await barrier.WaitForArrivalAsync();
            var secondTask = AcceptAsync(client, second, secondOffer);
            await barrier.WaitForBlockedByAsync(firstPid);
            await barrier.ReleaseAsync();
            responses = await Task.WhenAll(firstTask, secondTask);
        }

        _output.WriteLine("Final-seat acceptances: {0}", string.Join(", ", responses.Select(x => (int)x.StatusCode)));
        await AssertOneAdmittedAsync(responses);
        Assert.Equal(3, await CountActiveAsync(merchant.Shop));
        var loser = await MembershipAsync(merchant.Shop, second.UserId);
        Assert.Null(loser);
        Assert.Null(await AcceptedAtAsync(secondOffer.Id));
    }

    [Fact]
    public async Task TwoFinalSeatReinstatementsAdmitExactlyOne()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var merchant = await MerchantAsync(client, allowance: 3, active: 1);
        var suspended = await PersonAsync(client, merchant.Shop, MembershipStatus.Suspended);
        var removed = await PersonAsync(client, merchant.Shop, MembershipStatus.Removed);

        await using var holder = await HoldShopRowAsync(merchant.Shop);
        var reinstatements = new[]
        {
            PatchAsync(client, merchant, suspended.UserId, "Active", 1),
            PatchAsync(client, merchant, removed.UserId, "Active", 1, "Manager"),
        };
        // These contenders share the owner's account lock: one queues on the shop root, the other behind it.
        await WaitForShopWaitersAsync(holder, 1);
        await WaitForLockWaitersAsync(2);
        await holder.ReleaseAsync();
        var responses = await Task.WhenAll(reinstatements);

        _output.WriteLine("Final-seat reinstatements: {0}", string.Join(", ", responses.Select(x => (int)x.StatusCode)));
        await AssertOneAdmittedAsync(responses);
        Assert.Equal(3, await CountActiveAsync(merchant.Shop));
        var loser = await MembershipAsync(merchant.Shop,
            responses[0].IsSuccessStatusCode ? removed.UserId : suspended.UserId);
        Assert.NotEqual(MembershipStatus.Active, loser!.Status);
        Assert.Equal(1, loser.AuthorityVersion);
        Assert.Equal(ShopRole.Staff, loser.Role);
    }

    [Fact]
    public async Task AcceptanceAndReinstatementRaceForTheFinalSeat()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var merchant = await MerchantAsync(client, allowance: 3, active: 1);
        var suspended = await PersonAsync(client, merchant.Shop, MembershipStatus.Suspended);
        var invitee = await EnrollAsync(client, "racing");
        var offer = await InviteAsync(host, client, merchant, invitee.Email);

        await using var holder = await HoldShopRowAsync(merchant.Shop);
        var acceptance = AcceptAsync(client, invitee, offer);
        var reinstatement = PatchAsync(client, merchant, suspended.UserId, "Active", 1);
        // These contenders share the owner's account lock: one queues on the shop root, the other behind it.
        await WaitForShopWaitersAsync(holder, 1);
        await WaitForLockWaitersAsync(2);
        await holder.ReleaseAsync();
        var responses = await Task.WhenAll(acceptance, reinstatement);

        _output.WriteLine("Accept vs reinstate: {0}", string.Join(", ", responses.Select(x => (int)x.StatusCode)));
        await AssertOneAdmittedAsync(responses);
        Assert.Equal(3, await CountActiveAsync(merchant.Shop));
        if (!responses[0].IsSuccessStatusCode)
            Assert.Null(await AcceptedAtAsync(offer.Id));
    }

    [Fact]
    public async Task FinalSeatReinstatementsWithDisjointAccountLocksSerializeOnTheShop()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var merchant = await MerchantAsync(client, allowance: 3, active: 0);
        // A second Active owner gives each contender its own account-lock set, so only the shop
        // root can serialize the two seat decisions.
        var coOwner = await PersonAsync(client, merchant.Shop, MembershipStatus.Active, ShopRole.Owner);
        var suspended = await PersonAsync(client, merchant.Shop, MembershipStatus.Suspended);
        var removed = await PersonAsync(client, merchant.Shop, MembershipStatus.Removed);
        Assert.Equal(2, await CountActiveAsync(merchant.Shop));

        HttpResponseMessage[] responses;
        await using (var holder = await HoldShopRowAsync(merchant.Shop))
        {
            var reinstatements = new[]
            {
                PatchAsync(client, merchant, suspended.UserId, "Active", 1),
                PatchAsync(client, merchant, removed.UserId, "Active", 1, token: coOwner.Token),
            };
            await WaitForShopWaitersAsync(holder, 2);
            await holder.ReleaseAsync();
            responses = await Task.WhenAll(reinstatements);
        }

        _output.WriteLine("Disjoint-lock reinstatements: {0}", string.Join(", ", responses.Select(x => (int)x.StatusCode)));
        await AssertOneAdmittedAsync(responses);
        Assert.Equal(3, await CountActiveAsync(merchant.Shop));
    }

    private static async Task AssertOneAdmittedAsync(HttpResponseMessage[] responses)
    {
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        var refused = responses.Single(x => x.StatusCode != HttpStatusCode.OK);
        await AssertProblemAsync(refused, HttpStatusCode.Conflict, CapacityCode);
    }
}
