using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests
{
    [Fact]
    public async Task ExhaustedPreflightBudgetStillSchedulesRecoveryOfLostPost()
    {
        var (fixture, fake, package, request, _, _, payment) = await StartRazorpayRefundAsync();
        await using var _ = fixture;
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveAndReadReceiptAsync(finance, package.Account, request, 4500);
        fake.FailOrderReads = 12;
        for (var hour = 0; hour < 12; hour++)
        {
            fixture.At(Instant.AddHours(hour));
            await PumpRefundsAsync(fixture);
        }
        Assert.Equal(12L, await fixture.CountAsync("refund_observations", "reason='PreflightUnavailable'"));
        Assert.Equal(0L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(0, RefundPosts(fake));

        fake.TruncateWhen = call => call.EndsWith("/refund", StringComparison.Ordinal);
        fixture.At(Instant.AddHours(12));
        await PumpRefundsAsync(fixture);
        var remoteId = Assert.Single(fake.Refunds)["id"]!.GetValue<string>();
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Unknown' AND reason='DispatchUncertain'"));
        Assert.Equal(0, DirectRefundGets(fake, payment, remoteId));

        fixture.At(Instant.AddHours(13));
        await PumpRefundsAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
        Assert.Equal(1, DirectRefundGets(fake, payment, remoteId));
        Assert.Equal(1, RefundPosts(fake));
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
    }

    [Fact]
    public async Task HintWhileOriginalCredentialsAreMissingSurvivesUntilAutomaticRead()
    {
        var fake = new FakeRazorpay();
        var settings = RazorpayPlusSimulatorSettings(FakeRazorpay.KeyId, FakeRazorpay.KeySecret);
        AddRazorpayProfile(settings, 2, "rotated", "acc_min919", "Test", "min/2", "FixtureCredentials:Rotated",
            "rzp_test_rotated", "rotated-secret");
        settings["FixtureCredentials:Rotated:WebhookSecret"] = P0305Tests.Secret;
        settings["FixtureCredentials:Rotated:WebhookAccountId"] = P0305Tests.Account;
        await using var fixture = await P0609Tests.StartAsync(database, Instant, settings, razorpayHandler: fake);
        fake.Clock = () => fixture.Checkout.CheckoutClock.Now;
        using var owner = await fixture.Checkout.Trial.Catalogue.SignInOwnerAsync();
        await P0310Tests.SelectProfileAsync(owner, "rzp", 0);
        var (package, request, payment) = await BuyAndRequestRazorpayAsync(fixture, fake, "Credential recovery");
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var receipt = await ApproveAndReadReceiptAsync(finance, package.Account, request, 4500);
        for (var i = 0; i < 100; i++) fake.Refunds.Add(fake.RefundRecord(payment, Guid.NewGuid().ToString("N"), 100));
        fake.TruncateWhen = call => call.EndsWith("/refund", StringComparison.Ordinal);
        await PumpRefundsAsync(fixture);
        var remoteId = fake.Refunds.Single(r => r["receipt"]!.GetValue<string>() == receipt)["id"]!.GetValue<string>();

        var config = fixture.Hosts.Licensing.Services.GetRequiredService<IConfiguration>();
        config["FixtureCredentials:Rzp:KeySecret"] = null;
        using var ingress = fixture.Hosts.Licensing.CreateClient();
        using var hint = await P0305Tests.SendAsync(ingress, RefundEventBody(payment, remoteId, receipt, 4500), "evt_rotated_hint");
        Assert.Equal(HttpStatusCode.NoContent, hint.StatusCode);
        Assert.Equal(1L, await fixture.CountAsync("refund_work", $"candidate_reference='{remoteId}'"));
        for (var hour = 0; hour < 13; hour++)
        {
            fixture.At(Instant.AddHours(hour));
            await PumpRefundsAsync(fixture);
        }
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Unknown' AND reason='OriginalAdapterUnavailable'"));
        Assert.Equal(13L, await fixture.CountAsync("refund_observations", "reason='OriginalAdapterUnavailable'"));
        Assert.Equal(0, DirectRefundGets(fake, payment, remoteId));
        Assert.Equal(0L, await fixture.CountAsync("refund_bindings"));

        config["FixtureCredentials:Rzp:KeySecret"] = FakeRazorpay.KeySecret;
        fixture.At(Instant.AddHours(13));
        await PumpRefundsAsync(fixture);
        Assert.Equal(1, DirectRefundGets(fake, payment, remoteId));
        Assert.Equal(1, RefundPosts(fake));
        Assert.Equal(1L, await fixture.CountAsync("refund_bindings", $"reference='{remoteId}'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
    }
}
