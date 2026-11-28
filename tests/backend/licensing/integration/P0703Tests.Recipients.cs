using System.Net;
using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0703Tests
{
    [Theory]
    [InlineData("removal")]
    [InlineData("opt-out")]
    [InlineData("revocation")]
    [InlineData("address-change-on-retry")]
    public async Task LicensingMutationAndSmtpHandoffAreOrderedForRecipient(string change)
    {
        await using var peer = new P0702SmtpPeer();
        peer.Finish(true);
        var clock = new P0201Clock { Now = Start };
        await using var paid = await StartPeerFixtureAsync(peer, clock);
        var package = await QueuePaidReminderAsync(paid);
        await DrainMailAsync(paid);
        Assert.Equal(1, peer.Submissions);
        var user = await SignInAsync(paid.Hosts);
        var isMember = change is "removal" or "address-change-on-retry";
        var id = isMember
            ? await P0204Tests.InviteManagerAsync(package.Payer.Client, package.Account, user.Client, user.Email)
            : await OptInAsync(package, user);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var target = package with { Payer = user };
        Assert.Equal("Pending", await StateAsync(paid, target));
        var retry = change == "address-change-on-retry";
        if (retry)
        {
            peer.RejectNext();
            Assert.True(await DeliverAsync(paid));
            Assert.Equal("RetryPending", await StateAsync(paid, target));
            clock.Now = clock.Now.AddSeconds(30);
        }
        var gate = peer.PauseNextGreeting();
        var sending = DeliverAsync(paid);
        await gate.Connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal("Leased", await StateAsync(paid, target));
            await InvalidateRecipientAsync(paid, package, user, id, change);
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        Assert.True(await sending);
        Assert.False(await DeliverAsync(paid));
        Assert.Equal("Suppressed", await StateAsync(paid, target));
        Assert.Equal(retry ? 2 : 1, peer.Submissions);
    }

    private static async Task<Guid> OptInAsync(Package package, Merchant user)
    {
        using var approval = await package.Payer.Client.PostAsJsonAsync($"/api/billing/accounts/{package.Account:D}/notice-contacts",
            new ApproveBillingContactRequestV1(Guid.NewGuid(), user.UserId, user.Email, "en-IN"));
        approval.EnsureSuccessStatusCode();
        var contact = await approval.Content.ReadFromJsonAsync<BillingContactV1>();
        var path = $"/api/billing/notice-contacts/{contact!.ContactId:D}/consent";
        using var consent = await user.Client.PostAsJsonAsync(path,
            new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), user.Email, null, true, "en-IN"));
        consent.EnsureSuccessStatusCode();
        return contact.ContactId;
    }

    private static async Task InvalidateRecipientAsync(P0308Fixture paid, Package package, Merchant user, Guid id, string change)
    {
        var account = $"/api/billing/accounts/{package.Account:D}";
        if (change == "address-change-on-retry")
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(paid.Checkout.Delivery.MerchantDb, $"""
                UPDATE users SET email = '{P0202Fixture.UniqueEmail()}', is_email_verified = true WHERE id = '{user.UserId:D}'
                """);
            return;
        }
        using var response = change switch
        {
            "removal" => await package.Payer.Client.PostAsJsonAsync($"{account}/members/{id:D}/remove",
                new RemoveBillingManagerV1(Guid.NewGuid(), 1, "Fixture role removal")),
            "revocation" => await package.Payer.Client.PostAsJsonAsync($"{account}/notice-contacts/{id:D}/revoke",
                new RevokeBillingContactRequestV1(Guid.NewGuid())),
            _ => await user.Client.PostAsJsonAsync($"/api/billing/notice-contacts/{id:D}/consent",
                new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), user.Email, null, false, "en-IN")),
        };
        response.EnsureSuccessStatusCode();
        using var denied = await user.Client.GetAsync(account + "/notices");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.DoesNotContain("12000", await denied.Content.ReadAsStringAsync());
    }
}
