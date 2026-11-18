using System.Net;
using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>Composed acceptance over real PostgreSQL and signed delivery. Simulator funds, never live-provider proof.</summary>
public sealed partial class P0312Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task InterruptedFullPaymentRetriesOneCompletePackageBeforeAcknowledgement()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart });
        var purchase = await fixture.AcceptedQuoteAsync(3, ["North", "South", "Central"]);
        var (payer, _, _, quote) = purchase;
        var key = Guid.NewGuid();
        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created, key)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);
        await AssertPublicationRollbackAsync(fixture, purchase, attempt);
        AssertStatus(await VerifyAsync(payer.Client, quote, attempt), "Verified", "Fulfilled", "Applying");
        await AssertPackageAsync(fixture, purchase);
        var receipt = Assert.Single(await ConfirmationsAsync(purchase));
        await VerifyAsync(payer.Client, quote, attempt);
        Assert.Equal(attempt, (await StartAsync(payer.Client, quote, HttpStatusCode.OK, key)).GetProperty("attemptId").GetGuid());
        await ObserveAsync(fixture, purchase, "interruption-retried");
        await AssertLostAcknowledgementAsync(fixture, purchase);
        await AssertSameConfirmationsAsync(purchase, [receipt]);
        await AssertPackageAsync(fixture, purchase);
        await ObserveAsync(fixture, purchase, "interruption-applied");
    }

    private static async Task AssertPublicationRollbackAsync(P0302Fixture fixture, Purchase purchase, Guid attempt)
    {
        var outbox = await fixture.CountAsync("entitlement_outbox");
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
            "ALTER TABLE public.entitlement_snapshots ADD CONSTRAINT fixture_p0312_refuse CHECK (false) NOT VALID");
        try
        {
            using var failed = await purchase.Payer.Client.PostAsJsonAsync(VerifyPath(purchase.Quote, attempt), new { launcherOutcome = "Success" });
            Assert.Equal(CheckoutPaymentsV1.Unavailable, await P0206Fixture.CodeAsync(failed));
            foreach (var table in new[] { "verified_payments", "payment_fulfilments", "payment_fulfilment_shops", "payment_confirmations" })
                Assert.Equal(0L, await fixture.CountAsync(table));
            Assert.Equal(0L, await fixture.CountAsync("subscription_term_facts", "basis = 'Paid'"));
            Assert.Equal(outbox, await fixture.CountAsync("entitlement_outbox"));
            Assert.Equal(1L, await fixture.CountAsync("development_gateway_payments", "state = 'Captured'"));
            Assert.Empty(await ConfirmationsAsync(purchase));
            await ObserveAsync(fixture, purchase, "interruption-rolled-back");
        }
        finally
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
                "ALTER TABLE public.entitlement_snapshots DROP CONSTRAINT fixture_p0312_refuse");
        }
    }

    private static async Task AssertLostAcknowledgementAsync(P0302Fixture fixture, Purchase purchase)
    {
        await fixture.Delivery.DrainAsync(source: true);
        await AssertDeliveryAsync(fixture, purchase, 0);
        Assert.True(await fixture.Delivery.DeliverAsync(source: false));
        await AssertDeliveryAsync(fixture, purchase, 1);
        // Select the actual next acknowledgement, rather than assuming which shop the pump orders first.
        var id = (Guid)(await fixture.Delivery.MerchantScalarAsync("""
            SELECT event_id FROM entitlement_outbox WHERE status = 'Pending'
            ORDER BY next_attempt_at, event_id LIMIT 1
            """))!;
        var snapshot = Assert.Single(await SnapshotsAsync(fixture, purchase), s => s.EventId == id);
        await P0214Tests.LoseAcknowledgementAsync(fixture.Delivery, snapshot);
        await AssertDeliveryAsync(fixture, purchase, 1);
        await ObserveAsync(fixture, purchase, "interruption-ack-lost");
        fixture.Delivery.Transport.LeaseClock.Now += TimeSpan.FromSeconds(4);
        await fixture.Delivery.DrainAsync(source: false);
        await AssertDeliveryAsync(fixture, purchase, 3);
    }
}
