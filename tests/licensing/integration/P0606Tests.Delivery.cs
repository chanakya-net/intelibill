using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0606Tests
{
    [Fact]
    public async Task ReorderedProjectionAndLostAcknowledgementCannotCreateOrReportAnotherGrant()
    {
        using var key = RSA.Create(2048);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var (p, term) = await PrepareWithTradingNorthAsync(f);
        var permit = await IssueRealPermitAsync(f, p.Shops[2], EarlyStart, deliverObservation: true);
        var cutover = permit.ValidUntil;

        var id = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, cutover, "CourtesyBridge", cutover);
        var (manager, _) = await f.Checkout.Trial.Catalogue.ManagerAsync("delivery.manager@example.test");
        using var _ = manager;
        var preview = await AdminPreviewAsync(manager, p.Account, id);
        using var approved = await AdminApproveAsync(manager, p.Account, id, Guid.NewGuid(), preview,
            "Approved delivery ordering and lost-ack test");
        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);

        // Drain Prepare -> Prepared -> Consume -> Consumed (this publishes initial snapshots for North & South).
        await DrainAtAsync(f, EarlyStart, licensing: true);  // Prepare -> Merchant
        await DrainAtAsync(f, EarlyStart, licensing: false); // Prepared -> Licensing (queues Consume)
        await DrainAtAsync(f, EarlyStart, licensing: true);  // Consume -> Merchant (commits cutoff, queues Consumed)
        await DrainAtAsync(f, EarlyStart, licensing: false); // Consumed -> Licensing (commits handoff + courtesy grant, queues North & South snapshots)

        // Withhold North's Outgoing snapshot by delaying its outbox attempt, and jump straight to cutover B.
        await LicensingPostgreSqlFixture.ExecuteAsync(f.Hosts.LicensingOwner, $"""
            UPDATE public.entitlement_outbox o
            SET next_attempt_at = '{cutover.AddHours(2):O}'::timestamptz
            FROM public.entitlement_messages m
            WHERE m.event_id = o.event_id AND m.shop_id = '{p.Shops[2].ShopId}' AND o.status = 'Pending'
            """);

        // Advance to cutover B and trigger scan for South only (so South's IncomingPaid snapshot tries to project BEFORE North's Outgoing snapshot!).
        f.At(cutover);
        var sessionClock = f.Hosts.Fixture.Clock;
        var original = sessionClock.Now;
        try
        {
            sessionClock.Now = cutover;
            f.Checkout.Delivery.Transport.LeaseClock.Now = cutover.AddSeconds(1);
            await f.Checkout.Delivery.ScanAsync(true);
            // Delay any newly scanned North snapshot too so North stays withheld.
            await LicensingPostgreSqlFixture.ExecuteAsync(f.Hosts.LicensingOwner, $"""
                UPDATE public.entitlement_outbox o
                SET next_attempt_at = '{cutover.AddHours(2):O}'::timestamptz
                FROM public.entitlement_messages m
                WHERE m.event_id = o.event_id AND m.shop_id = '{p.Shops[2].ShopId}' AND o.status = 'Pending'
                """);
            await f.Checkout.Delivery.DrainAsync(true);

            // South's IncomingPaid snapshot got 423 Locked -> deferred cleanly as Pending (NOT DeadLetter) and South CANNOT trade on Paid!
            // (South's courtesy grant [C, B) expired at B with zero grace, so South is blocked with Licence.Expired.)
            Assert.Equal(0L, await f.CountAsync("entitlement_outbox", "status='DeadLetter'"));
            Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync(
                $"SELECT count(*) FROM public.merchant_coverage_exception_activations WHERE exception_id='{id}' AND leg='IncomingPaid'"));
            Assert.Equal("Licence.Expired", await P0604Tests.FencedTradingAsync(f, p.Shops[3].ShopId));

            // Release North's Outgoing snapshot AND South's deferred IncomingPaid snapshot.
            await LicensingPostgreSqlFixture.ExecuteAsync(f.Hosts.LicensingOwner, $"""
                UPDATE public.entitlement_outbox SET next_attempt_at = '{cutover:O}'::timestamptz WHERE status = 'Pending'
                """);
            f.Checkout.Delivery.Transport.LeaseClock.Now = cutover.AddMinutes(2);
            await f.Checkout.Delivery.DrainAsync(true); // North projects Outgoing, then South projects IncomingPaid
            await f.Checkout.Delivery.DrainAsync(true); // Second pass if South ran before North on first sweep

            // South is now locally activated on Merchant, BUT Merchant's Acknowledgements have NOT been delivered to Licensing yet!
            Assert.Equal("Licence.Expired", await P0604Tests.FencedTradingAsync(f, p.Shops[2].ShopId));
            Assert.Equal("Allowed", await P0604Tests.FencedTradingAsync(f, p.Shops[3].ShopId));
            Assert.Equal(1L, await f.Checkout.Delivery.MerchantScalarAsync(
                $"SELECT count(*) FROM public.merchant_coverage_exception_activations WHERE exception_id='{id}' AND leg='IncomingPaid'"));
        }
        finally { sessionClock.Now = original; }

        // Before acknowledgements reach Licensing, Licensing MUST report Applying (not Acknowledged)!
        var beforeAck = (await AdminPreviewAsync(manager, p.Account, id)).GetProperty("approval");
        Assert.Equal("Applying", beforeAck.GetProperty("incomingActivation").GetString());

        // Deliver Merchant acknowledgements to Licensing -> now Acknowledged!
        var origNow = sessionClock.Now;
        try
        {
            sessionClock.Now = cutover.AddMinutes(2);
            await f.Checkout.Delivery.DrainAsync(false);
        }
        finally { sessionClock.Now = origNow; }

        var afterAck = (await AdminPreviewAsync(manager, p.Account, id)).GetProperty("approval");
        Assert.Equal("Acknowledged", afterAck.GetProperty("incomingActivation").GetString());
        Assert.Equal("Acknowledged", afterAck.GetProperty("outgoingActivation").GetString());

        // Simulate lost acknowledgement / duplicate delivery: re-queue the merchant acknowledgement outbox and drain again.
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), $"""
            UPDATE public.entitlement_outbox o SET status = 'Pending', attempts = 0, next_attempt_at = '{cutover:O}'::timestamptz
            FROM public.entitlement_messages m
            WHERE m.event_id = o.event_id AND o.account_id = '{p.Account}' AND m.kind = 'Acknowledgement' AND o.status = 'Delivered'
            """);
        await DrainAtAsync(f, cutover.AddMinutes(3), licensing: false);

        // Idempotent: still exactly ONE handoff and ONE courtesy grant.
        Assert.Equal(1L, await f.CountAsync("paid_slot_handoffs", $"exception_id='{id}'"));
        Assert.Equal(1L, await f.CountAsync("complimentary_grants", $"account_id='{p.Account}'"));
    }
}
