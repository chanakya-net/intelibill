using System.Net;
using System.Security.Cryptography;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0606Tests
{
    [Fact]
    public async Task PermitIssuanceAndEarlyPaidCutoverSerializeInBothOrders()
    {
        using var key = RSA.Create(2048);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var (p, term) = await PrepareWithTradingNorthAsync(f);

        // Order 1: Exception requested & approved assuming no active permit (observedPermitDeadline = null),
        // while a real offline permit is issued on Merchant whose device observation has NOT been delivered to Licensing.
        var cutover = EarlyStart.AddHours(4);
        var withheldId = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, cutover, "CourtesyBridge", observedPermitDeadline: null);
        var hiddenPermit = await IssueRealPermitAsync(f, p.Shops[2], EarlyStart, deliverObservation: false);

        var (manager, _) = await f.Checkout.Trial.Catalogue.ManagerAsync("race.manager@example.test");
        using var _ = manager;
        var withheldPreview = await AdminPreviewAsync(manager, p.Account, withheldId);
        using var approveWithheld = await AdminApproveAsync(manager, p.Account, withheldId, Guid.NewGuid(),
            withheldPreview, "Early switch before observation uploaded");
        Assert.Equal(HttpStatusCode.Created, approveWithheld.StatusCode);

        // Drain Prepare to Merchant, then Receipt back to Licensing.
        // Merchant fence inspection sees the hidden permit and refuses Prepare with UnseenPermitEvidence/PermitExceedsCutover.
        await DrainAtAsync(f, EarlyStart, licensing: true);
        await DrainAtAsync(f, EarlyStart, licensing: false);

        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exceptions",
            $"exception_id='{withheldId}' AND state='Blocked' AND reason IN ('UnseenPermitEvidence','PermitExceedsCutover')"));
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals",
            $"exception_id='{withheldId}' AND state='Refused' AND outcome_reason IN ('UnseenPermitEvidence','PermitExceedsCutover')"));
        Assert.Equal(0L, await f.CountAsync("paid_slot_handoffs"));
        Assert.Equal(0L, await f.CountAsync("complimentary_grants"));
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync("SELECT count(*) FROM public.merchant_coverage_exception_cutoffs"));

        // Order 2: Permit issued BETWEEN Prepare and Consume -> Consume sees changed permit digest and refuses!
        // The hidden observation was just delivered above; request with a later cutover (hiddenPermit.ValidUntil + 2h).
        var raceCutover = hiddenPermit.ValidUntil.AddHours(2);
        var raceId = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, raceCutover, "CourtesyBridge",
            observedPermitDeadline: hiddenPermit.ValidUntil);
        var racePreview = await AdminPreviewAsync(manager, p.Account, raceId);
        using var approveRace = await AdminApproveAsync(manager, p.Account, raceId, Guid.NewGuid(),
            racePreview, "Approve with observed permit; second permit races before consume");
        Assert.Equal(HttpStatusCode.Created, approveRace.StatusCode);

        // Step 1: Deliver Prepare ONLY to Merchant (Merchant returns Prepared).
        await DrainAtAsync(f, EarlyStart, licensing: true);
        Assert.Equal(1L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM public.merchant_coverage_exception_decisions WHERE exception_id='{raceId}' AND outcome='Prepared'"));

        // Step 2: Before Consume executes on Merchant, issue a SECOND permit on North (changes permit digest & max end!).
        var secondPermit = await IssueRealPermitAsync(f, p.Shops[2], EarlyStart.AddMinutes(5), deliverObservation: false);
        Assert.True(secondPermit.ValidUntil >= hiddenPermit.ValidUntil);

        // Step 3: Drain Prepared receipt to Licensing -> Licensing queues Consume -> Consume reaches Merchant -> Refused!
        await DrainAtAsync(f, EarlyStart.AddMinutes(5), licensing: false);
        await DrainAtAsync(f, EarlyStart.AddMinutes(5), licensing: true);
        await DrainAtAsync(f, EarlyStart.AddMinutes(5), licensing: false);

        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exceptions", $"exception_id='{raceId}' AND state='Blocked'"));
        Assert.Equal(0L, await f.CountAsync("paid_slot_handoffs"));
        Assert.Equal(0L, await f.CountAsync("complimentary_grants"));
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync("SELECT count(*) FROM public.merchant_coverage_exception_cutoffs"));

        // Order 3: Consume wins -> durable cutoff committed on Merchant -> late permit issuance capped at <= cutoff
        // and direct DB INSERT exceeding cutoff is rejected by trigger guard_offline_permit_exception_cutoff (23514)!
        await DrainAtAsync(f, EarlyStart.AddMinutes(10), licensing: false);
        var finalCutover = secondPermit.ValidUntil;
        var winId = await CreateConsentedExceptionAsync(f, p, term, EarlyStart.AddMinutes(10), finalCutover,
            "CourtesyBridge", observedPermitDeadline: finalCutover);
        var winPreview = await AdminPreviewAsync(manager, p.Account, winId);
        using var approveWin = await AdminApproveAsync(manager, p.Account, winId, Guid.NewGuid(),
            winPreview, "Final approved handoff after all observed permits");
        Assert.Equal(HttpStatusCode.Created, approveWin.StatusCode);
        await DrainExceptionProtocolAsync(f, EarlyStart.AddMinutes(10));

        Assert.Equal(1L, await f.CountAsync("paid_slot_handoffs", $"exception_id='{winId}'"));
        Assert.Equal(1L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM public.merchant_coverage_exception_cutoffs WHERE exception_id='{winId}'"));

        // Issuing another permit on North while still before finalCutover caps its ValidUntil at <= finalCutover.
        var latePermit = await IssueRealPermitAsync(f, p.Shops[2], EarlyStart.AddMinutes(15), deliverObservation: false);
        Assert.True(latePermit.ValidUntil <= finalCutover,
            $"Late permit {latePermit.ValidUntil:O} exceeded cutoff {finalCutover:O}");

        // Direct DB INSERT attempting to store an offline permit past the cutoff fails with SQLSTATE 23514.
        var merchantOwner = database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase);
        var pg = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(merchantOwner, $"""
            INSERT INTO public.offline_license_permits (
                id, shop_id, grant_id, lease_id, snapshot_id, user_id, session_id, device_id,
                batch_count, catalog_version, current_account_revision, current_shop_revision,
                completed_at, not_before, valid_until, limiting_reason, key_id, signed_permit)
            SELECT gen_random_uuid(), shop_id, grant_id, lease_id, gen_random_uuid(), user_id, session_id, device_id,
                batch_count, catalog_version, current_account_revision, current_shop_revision,
                completed_at, not_before, '{finalCutover.AddMinutes(1):O}'::timestamptz, limiting_reason, key_id, signed_permit
            FROM public.offline_license_permits WHERE shop_id = '{p.Shops[2].ShopId}' LIMIT 1
            """));
        Assert.Equal("23514", pg.SqlState);
    }

    internal static async Task DrainAtAsync(P0308Fixture f, DateTimeOffset at, bool licensing)
    {
        var sessionClock = f.Hosts.Fixture.Clock;
        var original = sessionClock.Now;
        try
        {
            sessionClock.Now = at;
            f.Checkout.Delivery.Transport.LeaseClock.Now = at.AddSeconds(1);
            await f.Checkout.Delivery.DrainAsync(licensing);
        }
        finally { sessionClock.Now = original; }
    }

    internal static async Task DrainExceptionProtocolAsync(P0308Fixture f, DateTimeOffset? at = null)
    {
        var instant = at ?? f.Checkout.CheckoutClock.Now;
        for (var i = 0; i < 4; i++)
        {
            await DrainAtAsync(f, instant, licensing: true);
            await DrainAtAsync(f, instant, licensing: false);
        }
    }
}
