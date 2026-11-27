using System.Net;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests
{
    [Fact]
    public async Task SignedRefundHintIsDeduplicatedWithoutCreatingARefund()
    {
        var options = new Dictionary<string, string?>
        {
            ["Payments:Razorpay:WebhookSecret"] = P0305Tests.Secret,
            ["Payments:Razorpay:WebhookAccountId"] = P0305Tests.Account,
        };
        await using var rig = await P0304Tests.Rig.StartAsync(database, extraSettings: options);
        using var client = rig.Fixture.Hosts.Licensing.CreateClient();
        var body = JsonSerializer.Serialize(new
        {
            entity = "event",
            account_id = P0305Tests.Account,
            @event = "refund.processed",
            payload = new
            {
                refund = new
                {
                    entity = new
                    {
                        entity = "refund",
                        id = "rfnd_fixture",
                        payment_id = "pay_fixture",
                        receipt = Guid.NewGuid().ToString("N"),
                        amount = 4500,
                        currency = "INR",
                        status = "processed"
                    }
                }
            },
        });
        foreach (var _ in Enumerable.Range(0, 2))
        {
            using var result = await P0305Tests.SendAsync(client, body, "refund_event");
            Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        }
        await P0305Tests.DrainAsync(rig.Fixture.Hosts.Licensing.Services, rig.Fixture.CheckoutClock);
        Assert.Equal(1L, await rig.Fixture.CountAsync("gateway_inbox_events", "kind='RefundChanged'"));
        Assert.Equal(1L, await rig.Fixture.CountAsync("gateway_inbox_work", "status='Processed'"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("refund_sends"));
    }

    [Fact]
    public async Task AuthenticatedHintWakesOnlyItsOriginalSettledRefund()
    {
        var settings = P0310Tests.WebhookSettings();
        await using var fixture = await P0609Tests.StartAsync(database, P0609Tests.Instant, settings);
        using var owner = await fixture.Checkout.Trial.Catalogue.SignInOwnerAsync();
        await P0310Tests.SelectProfileAsync(owner, "a", 0);
        var (package, request, _, _) = await P0609Tests.RequestedAsync(fixture, 6000);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, request, P0609Tests.Instant.AddDays(2));
        await PumpRefundsAsync(fixture);
        var receipt = (string)(await fixture.ScalarAsync("SELECT receipt FROM development_gateway_refunds"))!;
        var payment = (string)(await fixture.ScalarAsync("SELECT payment_reference FROM development_gateway_refunds"))!;
        var reference = (string)(await fixture.ScalarAsync("SELECT reference FROM development_gateway_refunds"))!;
        await P0310Tests.SelectProfileAsync(owner, "b", 1);
        using var client = fixture.Hosts.Licensing.CreateClient();
        async Task HintAsync(string id, string paymentId)
        {
            var body = JsonSerializer.Serialize(new
            {
                kind = "RefundChanged",
                paymentReference = paymentId,
                receipt,
                refundReference = reference
            });
            using var response = await P0310Tests.SignedSimulatorEventAsync(client, body, id, P0310Tests.SecretA);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        await HintAsync("other-payment", payment + "x");
        await PumpRefundsAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("refund_observations"));
        // A later provider failure is evidence. The verified settlement and application remain immutable.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, """
            INSERT INTO development_refund_controls(receipt,provider_account_id,environment,configuration_version,outcome)
            SELECT receipt,provider_account_id,environment,configuration_version,'Failed' FROM development_gateway_refunds
            """);
        await HintAsync("settled-refund", payment);
        await HintAsync("settled-refund", payment);
        await Task.WhenAll(PumpRefundsAsync(fixture), PumpRefundsAsync(fixture));
        Assert.Equal(1L, await fixture.CountAsync("refund_observations", "outcome='Confirmed'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_observations", "outcome='Failed'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Confirmed' AND reason='ConflictingProviderState'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications"));
    }
}
