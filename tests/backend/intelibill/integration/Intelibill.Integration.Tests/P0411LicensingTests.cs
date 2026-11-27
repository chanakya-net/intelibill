using System.Net;
using Intelibill.Domain.Enums;

namespace Intelibill.Integration.Tests;

public sealed partial class P0411LicensingTests
{
    private const string CapacityCode = "Licence.MemberCapacityReached";

    [Fact]
    public async Task AcceptanceAtTheAllowanceIsRefusedAndTheOfferStaysPending()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var merchant = await MerchantAsync(client, allowance: 3, active: 2);
        var invitee = await EnrollAsync(client, "invitee");
        var offer = await InviteAsync(host, client, merchant, invitee.Email);

        var refused = await AssertProblemAsync(await AcceptAsync(client, invitee, offer), HttpStatusCode.Conflict, CapacityCode);
        Assert.False(refused.TryGetProperty("memberCapacity", out _), "An invitee must not learn the shop's seat usage.");
        Assert.Null(await MembershipAsync(merchant.Shop, invitee.UserId));
        Assert.Null(await AcceptedAtAsync(offer.Id));
        Assert.Equal(3, await CountActiveAsync(merchant.Shop));

        // Suspending a member frees a seat; the same, still-pending offer is then redeemable.
        var staff = await FirstStaffAsync(merchant.Shop);
        await ReadSuccessAsync(await PatchAsync(client, merchant, staff, "Suspended", 0));
        var admitted = await ReadSuccessAsync(await AcceptAsync(client, invitee, offer));
        Assert.Equal("Active", admitted.GetProperty("status").GetString());
        Assert.Equal(3, await CountActiveAsync(merchant.Shop));
        Assert.NotNull(await AcceptedAtAsync(offer.Id));
    }

    [Fact]
    public async Task ReinstatementConsumesASeat()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var merchant = await MerchantAsync(client, allowance: 3, active: 2);
        var suspended = await EnrollAsync(client, "suspended");
        var removed = await EnrollAsync(client, "removed");
        var suspendedVersion = await SeedAsync(merchant.Shop, suspended.UserId, MembershipStatus.Suspended);
        var removedVersion = await SeedAsync(merchant.Shop, removed.UserId, MembershipStatus.Removed);

        await AssertProblemAsync(await PatchAsync(client, merchant, suspended.UserId, "Active", suspendedVersion),
            HttpStatusCode.Conflict, CapacityCode);
        await AssertProblemAsync(await PatchAsync(client, merchant, removed.UserId, "Active", removedVersion, "Manager"),
            HttpStatusCode.Conflict, CapacityCode);
        var offer = await InviteAsync(host, client, merchant, removed.Email);
        await AssertProblemAsync(await AcceptAsync(client, removed, offer), HttpStatusCode.Conflict, CapacityCode);

        var unchanged = await MembershipAsync(merchant.Shop, suspended.UserId);
        Assert.Equal((MembershipStatus.Suspended, suspendedVersion), (unchanged!.Status, unchanged.AuthorityVersion));
        Assert.Equal(MembershipStatus.Removed, (await MembershipAsync(merchant.Shop, removed.UserId))!.Status);
        Assert.Null(await AcceptedAtAsync(offer.Id));

        // Removal frees a seat: the refused reinstatement then succeeds.
        await ReadSuccessAsync(await PatchAsync(client, merchant, await FirstStaffAsync(merchant.Shop), "Removed", 0));
        var reinstated = await ReadSuccessAsync(await PatchAsync(client, merchant, suspended.UserId, "Active", suspendedVersion));
        Assert.Equal("Active", reinstated.GetProperty("status").GetString());
        Assert.Equal(3, await CountActiveAsync(merchant.Shop));
    }

    [Fact]
    public async Task RoleOnlyChangesAndExistingMembersUseNoNewSeat()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var full = await MerchantAsync(client, allowance: 3, active: 2);
        // A later revision lowers the allowance below the live count.
        await ApplyAsync(full.Binding, _clock.Now.AddDays(30), revision: 2, allowance: 2);

        var staff = await FirstStaffAsync(full.Shop);
        var promoted = await ReadSuccessAsync(await PatchAsync(client, full, staff, "Active", 0, "Manager"));
        Assert.Equal("Manager", promoted.GetProperty("role").GetString());
        await ReadSuccessAsync(await PatchAsync(client, full, staff, "Active", 1, "Staff"));
        Assert.Equal(3, await CountActiveAsync(full.Shop));

        // An Active member of the full shop takes exactly one seat in another shop.
        var other = await MerchantAsync(client, allowance: 2, active: 0);
        var person = await PersonAsync(client, full.Shop, MembershipStatus.Active);
        var offer = await InviteAsync(host, client, other, person.Email);
        await ReadSuccessAsync(await AcceptAsync(client, person, offer));
        Assert.Equal(2, await CountActiveAsync(other.Shop));
        Assert.Equal(4, await CountActiveAsync(full.Shop));
    }

    [Fact]
    public async Task LowerAllowanceRemovesNobodyAndFreedSeatsAdmitAgain()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var merchant = await MerchantAsync(client, allowance: 3, active: 2);
        var invitee = await EnrollAsync(client, "invitee");
        var offer = await InviteAsync(host, client, merchant, invitee.Email);

        await ApplyAsync(merchant.Binding, _clock.Now.AddDays(30), revision: 2, allowance: 2);
        var staff = await StaffAsync(merchant.Shop);
        foreach (var user in staff)
        {
            var kept = await MembershipAsync(merchant.Shop, user);
            Assert.Equal((MembershipStatus.Active, ShopRole.Staff, 0L), (kept!.Status, kept.Role, kept.AuthorityVersion));
        }
        Assert.Equal(3, await CountActiveAsync(merchant.Shop));
        await AssertProblemAsync(await AcceptAsync(client, invitee, offer), HttpStatusCode.Conflict, CapacityCode);

        await ReadSuccessAsync(await PatchAsync(client, merchant, staff[0], "Suspended", 0));
        await AssertProblemAsync(await AcceptAsync(client, invitee, offer), HttpStatusCode.Conflict, CapacityCode);
        await ReadSuccessAsync(await PatchAsync(client, merchant, staff[1], "Removed", 0));
        await ReadSuccessAsync(await AcceptAsync(client, invitee, offer));
        Assert.Equal(2, await CountActiveAsync(merchant.Shop));
    }

    [Fact]
    public async Task PendingInvitationsConsumeNoSeat()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var merchant = await MerchantAsync(client, allowance: 3, active: 1);
        var invitees = new List<Person>();
        var offers = new List<(Guid Id, string Token)>();
        for (var i = 0; i < 3; i++)
        {
            invitees.Add(await EnrollAsync(client, $"pending{i}"));
            offers.Add(await InviteAsync(host, client, merchant, invitees[i].Email));
        }

        Assert.Equal(2, await CountActiveAsync(merchant.Shop));
        await ReadSuccessAsync(await AcceptAsync(client, invitees[0], offers[0]));
        await AssertProblemAsync(await AcceptAsync(client, invitees[1], offers[1]), HttpStatusCode.Conflict, CapacityCode);
        await AssertProblemAsync(await AcceptAsync(client, invitees[2], offers[2]), HttpStatusCode.Conflict, CapacityCode);
        Assert.Equal(3, await CountActiveAsync(merchant.Shop));
    }

    [Fact]
    public async Task CapacityDetailsReachOnlyTheOwnerAndAuthorityRulesComeFirst()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var merchant = await MerchantAsync(client, allowance: 3, active: 2);
        var suspended = await PersonAsync(client, merchant.Shop, MembershipStatus.Suspended);

        var stale = await PatchAsync(client, merchant, suspended.UserId, "Active", 0);
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "Users.MembershipVersionConflict");
        // Ownership is never administered here, even when the person holding it is not seated.
        var formerOwner = await PersonAsync(client, merchant.Shop, MembershipStatus.Suspended, ShopRole.Owner);
        await AssertProblemAsync(await PatchAsync(client, merchant, formerOwner.UserId, "Active", 1, "Manager"),
            HttpStatusCode.Forbidden, "Users.CannotModifyOwner");
        Assert.Equal(MembershipStatus.Suspended, (await MembershipAsync(merchant.Shop, formerOwner.UserId))!.Status);

        var refused = await AssertProblemAsync(await PatchAsync(client, merchant, suspended.UserId, "Active", 1),
            HttpStatusCode.Conflict, CapacityCode);
        var capacity = refused.GetProperty("memberCapacity");
        Assert.Equal(3, capacity.GetProperty("memberAllowance").GetInt32());
        Assert.Equal(3, capacity.GetProperty("activeMembers").GetInt32());

        var invitee = await EnrollAsync(client, "invitee");
        var bystander = await EnrollAsync(client, "bystander");
        var offer = await InviteAsync(host, client, merchant, invitee.Email);
        await AssertProblemAsync(await AcceptAsync(client, bystander, offer), HttpStatusCode.Forbidden,
            "Users.InvitationWrongAccount");
        await AssertProblemAsync(await AcceptAsync(client, invitee, offer with { Token = "forged" }),
            HttpStatusCode.BadRequest, "Users.InvitationNotAcceptable");
        _output.WriteLine("Owner capacity conflict: {0}", refused);
    }
}
