using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0606Tests
{
    [Fact]
    public async Task RevokingConsentDuringPrepareAbortsOrRefusesWithoutAllocating()
    {
        using var key = RSA.Create(2048);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var (p, term) = await PrepareWithTradingNorthAsync(f);
        var permit = await IssueRealPermitAsync(f, p.Shops[2], EarlyStart, deliverObservation: true);
        var cutover = permit.ValidUntil;
        var (manager, _) = await f.Checkout.Trial.Catalogue.ManagerAsync("r004rev.manager@example.test");
        using var _ = manager;

        // Path A: Revocation reaches Licensing while Preparing -> AbortPreparingForRevocationAsync -> Aborted + Blocked.
        var id1 = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, cutover, "CourtesyBridge", cutover);
        using (var approve1 = await AdminApproveAsync(manager, p.Account, id1, Guid.NewGuid(),
            await AdminPreviewAsync(manager, p.Account, id1), "Revoke delivered to Licensing while Preparing"))
            Assert.Equal(HttpStatusCode.Created, approve1.StatusCode);

        using (var rev1 = await P0604Tests.Decide(p.Shops[2].Client, OwnerConsentPath(p, p.Shops[2].ShopId, id1), "revoke"))
            Assert.Equal(HttpStatusCode.OK, rev1.StatusCode);
        await DrainAtAsync(f, EarlyStart, licensing: false); // Revoke -> Licensing transitions Preparing -> Aborting
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals",
            $"exception_id='{id1}' AND state='Aborting' AND outcome_reason='ConsentRevoked'"));
        await DrainAtAsync(f, EarlyStart, licensing: true);  // Prepare + Abort -> Merchant
        await DrainAtAsync(f, EarlyStart, licensing: false); // Receipts -> HandleAbortReceiptAsync
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals",
            $"exception_id='{id1}' AND state='Aborted' AND outcome_reason='ConsentRevoked'"));
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exceptions",
            $"exception_id='{id1}' AND state='Blocked' AND reason='ConsentRevoked'"));
        await AssertZeroExceptionEffectsAsync(f, p.Account);

        // Path B: Revocation sits on Merchant when Prepare arrives -> exception_fence_inspect refuses with OutgoingConsentInvalid.
        var id2 = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, cutover, "CourtesyBridge", cutover);
        using (var approve2 = await AdminApproveAsync(manager, p.Account, id2, Guid.NewGuid(),
            await AdminPreviewAsync(manager, p.Account, id2), "Prepare hits Merchant after local revocation"))
            Assert.Equal(HttpStatusCode.Created, approve2.StatusCode);

        using (var rev2 = await P0604Tests.Decide(p.Shops[2].Client, OwnerConsentPath(p, p.Shops[2].ShopId, id2), "revoke"))
            Assert.Equal(HttpStatusCode.OK, rev2.StatusCode);
        await DrainAtAsync(f, EarlyStart, licensing: true); // Prepare reaches Merchant BEFORE revoke reaches Licensing
        Assert.Equal(1L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM public.merchant_coverage_exception_decisions WHERE exception_id='{id2}' AND phase='CoverageExceptionPrepare' AND outcome='Refused' AND reason='OutgoingConsentInvalid'"));
        await DrainExceptionProtocolAsync(f, EarlyStart);
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals",
            $"exception_id='{id2}' AND state IN ('Refused','Aborted')"));
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exceptions",
            $"exception_id='{id2}' AND state='Blocked'"));
        await AssertZeroExceptionEffectsAsync(f, p.Account);
        await AssertOriginalRosterTradingAsync(f, p, EarlyStart);
    }

    [Fact]
    public async Task OwnerAuthorityChangeBeforeConsumeRefusesWithoutAllocating()
    {
        using var key = RSA.Create(2048);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var (p, term) = await PrepareWithTradingNorthAsync(f);
        var cutover = EarlyStart.AddHours(4);
        var (manager, _) = await f.Checkout.Trial.Catalogue.ManagerAsync("r004auth.manager@example.test");
        using var _ = manager;
        var merchantOwner = database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase);

        // (1) Outgoing owner replaced / authority_version incremented between Prepared and Consume.
        await AssertOwnerDriftRefusesConsumeAsync(f, p, term, manager, merchantOwner, cutover,
            driftedShop: p.Shops[2], expectedReason: "OutgoingConsentInvalid");
        // (2) Incoming owner replaced / authority_version incremented between Prepared and Consume.
        await AssertOwnerDriftRefusesConsumeAsync(f, p, term, manager, merchantOwner, cutover,
            driftedShop: p.Shops[3], expectedReason: "IncomingConsentInvalid");
        await AssertOriginalRosterTradingAsync(f, p, EarlyStart);
    }

    [Fact]
    public async Task BillingOwnerRotationAfterApprovalAbortsPrepareReceipt()
    {
        using var key = RSA.Create(2048);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var (p, term) = await PrepareWithTradingNorthAsync(f);
        var cutover = EarlyStart.AddHours(4);
        var (manager, _) = await f.Checkout.Trial.Catalogue.ManagerAsync("r004rot.manager@example.test");
        using var _ = manager;

        var id = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, cutover, "CourtesyBridge", null);
        using (var approved = await AdminApproveAsync(manager, p.Account, id, Guid.NewGuid(),
            await AdminPreviewAsync(manager, p.Account, id), "Approve before billing owner rotates"))
            Assert.Equal(HttpStatusCode.Created, approved.StatusCode);

        // Prepare succeeds on Merchant before the Billing Owner rotates on Licensing.
        await DrainAtAsync(f, EarlyStart, licensing: true);
        Assert.Equal(1L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM public.merchant_coverage_exception_decisions WHERE exception_id='{id}' AND outcome='Prepared'"));

        await LicensingPostgreSqlFixture.ExecuteAsync(f.Hosts.LicensingOwner, $"""
            INSERT INTO billing_account_members(id,account_id,merchant_user_id,role,status,revision,created_at)
            VALUES(gen_random_uuid(),'{p.Account}','{p.Shops[1].UserId}',2,1,1,now());
            UPDATE billing_account_members SET role=CASE WHEN merchant_user_id='{p.Payer.UserId}' THEN 2 ELSE 1 END
            WHERE account_id='{p.Account}' AND merchant_user_id IN ('{p.Payer.UserId}','{p.Shops[1].UserId}')
            """);

        // Prepared receipt arrives at Licensing -> CheckPreparedStillValidAsync sees BillingOwnerChanged -> Aborting -> Aborted.
        await DrainAtAsync(f, EarlyStart, licensing: false);
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals",
            $"exception_id='{id}' AND state='Aborting' AND outcome_reason='BillingOwnerChanged'"));
        await DrainAtAsync(f, EarlyStart, licensing: true);  // Abort -> Merchant
        await DrainAtAsync(f, EarlyStart, licensing: false); // Aborted receipt -> Licensing
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals",
            $"exception_id='{id}' AND state='Aborted' AND outcome_reason='BillingOwnerChanged'"));
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exceptions",
            $"exception_id='{id}' AND state='Blocked' AND reason='BillingOwnerChanged'"));
        await AssertZeroExceptionEffectsAsync(f, p.Account);
        await AssertOriginalRosterTradingAsync(f, p, EarlyStart);
    }

    [Fact]
    public async Task InFlightExceptionBlocksCompetingSlotMutationsUntilDefinitiveAbort()
    {
        using var key = RSA.Create(2048);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var p = await f.PackageAsync(4, ["Central", "East", "North", "South"]);
        var (_, term) = await PurchaseAsync(p with { Shops = p.Shops[..2] }, "Month"); // 3-slot plan, 2 covered + 1 spare
        await f.Checkout.Delivery.DrainAsync(true);
        await f.Checkout.Delivery.DrainAsync(false);
        f.At(EarlyStart);

        var cutover = EarlyStart.AddHours(4);
        var id = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, cutover, "CourtesyBridge", null,
            outgoingShop: p.Shops[1], incomingShop: p.Shops[2]);
        var (manager, _) = await f.Checkout.Trial.Catalogue.ManagerAsync("r004busy.manager@example.test");
        using var _ = manager;
        using (var approved = await AdminApproveAsync(manager, p.Account, id, Guid.NewGuid(),
            await AdminPreviewAsync(manager, p.Account, id), "Hold exception Preparing to block competing mutations"))
            Assert.Equal(HttpStatusCode.Created, approved.StatusCode);

        // While Preparing: spare-slot assignment, #913 scheduled replacement, renewal quote, overlapping paid purchase,
        // and overlapping complimentary grant are all refused.
        using (var slotBusy = await p.Payer.Client.PostAsJsonAsync(P0604Tests.Slots(p, term), P0604Tests.Request(p.Shops[3].ShopId, Guid.NewGuid())))
            Assert.Equal("Coverage.SlotUnavailable", await P0206Fixture.CodeAsync(slotBusy));
        using (var repBusy = await p.Payer.Client.PostAsJsonAsync(P0605Tests.PathFor(p, term),
            BuildScheduledReplacement(p.Shops[1].ShopId, p.Shops[3].ShopId)))
            Assert.Equal(HttpStatusCode.Conflict, repBusy.StatusCode);
        using (var renewBusy = await TryRenewalQuoteAsync(p, term, "Month"))
            Assert.Equal(HttpStatusCode.BadRequest, renewBusy.StatusCode);
        using (var buyIncomingBusy = await p.Payer.Client.PostAsJsonAsync(P0301Tests.OrdersPath(p.Account),
            new Intelibill.Shared.Contracts.V1.CreateCheckoutQuoteV1(Guid.NewGuid(), p.Plan, "Month", [p.Shops[2].ShopId], 0, 1)))
            Assert.Equal(HttpStatusCode.BadRequest, buyIncomingBusy.StatusCode);
        var grantPreview = await P0212Fixture.PreviewAsync(manager, p.Account,
            P0212Fixture.Draft([p.Shops[2].ShopId], EarlyStart, cutover, memberAllowance: 5, reason: "Competing overlap"));
        using (var grantBusy = await P0212Fixture.PostAsync(manager, P0212Fixture.Base(p.Account),
            P0212Fixture.Confirm(grantPreview, Guid.NewGuid())))
            Assert.Equal(HttpStatusCode.Conflict, grantBusy.StatusCode);

        // Definitively abort the in-flight exception via consent revocation.
        using (var rev = await P0604Tests.Decide(p.Shops[1].Client, OwnerConsentPath(p, p.Shops[1].ShopId, id), "revoke"))
            Assert.Equal(HttpStatusCode.OK, rev.StatusCode);
        await DrainAtAsync(f, EarlyStart, licensing: false);
        await DrainAtAsync(f, EarlyStart, licensing: true);
        await DrainAtAsync(f, EarlyStart, licensing: false);
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals", $"exception_id='{id}' AND state='Aborted'"));
        await AssertZeroExceptionEffectsAsync(f, p.Account);

        // After definitive abort, all competing operations work again.
        using (var renewOk = await TryRenewalQuoteAsync(p, term, "Month"))
            Assert.Equal(HttpStatusCode.Created, renewOk.StatusCode);
        using (var repOk = await p.Payer.Client.PostAsJsonAsync(P0605Tests.PathFor(p, term),
            BuildScheduledReplacement(p.Shops[1].ShopId, p.Shops[2].ShopId)))
            Assert.Equal(HttpStatusCode.OK, repOk.StatusCode);
        using (var slotOk = await p.Payer.Client.PostAsJsonAsync(P0604Tests.Slots(p, term),
            P0604Tests.Request(p.Shops[3].ShopId, Guid.NewGuid())))
            Assert.Equal(HttpStatusCode.OK, slotOk.StatusCode);
    }

    private static async Task AssertOwnerDriftRefusesConsumeAsync(P0308Fixture f, Package p, Guid term,
        P0110CookieBrowser manager, Npgsql.NpgsqlConnectionStringBuilder merchantOwner, DateTimeOffset cutover,
        P0209Fixture.Merchant driftedShop, string expectedReason)
    {
        var id = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, cutover, "CourtesyBridge", null);
        using (var approved = await AdminApproveAsync(manager, p.Account, id, Guid.NewGuid(),
            await AdminPreviewAsync(manager, p.Account, id), $"Test {expectedReason} before consume"))
            Assert.Equal(HttpStatusCode.Created, approved.StatusCode);

        await DrainAtAsync(f, EarlyStart, licensing: true);  // Prepare -> Prepared
        await DrainAtAsync(f, EarlyStart, licensing: false); // Prepared -> queues Consume
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals", $"exception_id='{id}' AND state='Consuming'"));

        await LicensingPostgreSqlFixture.ExecuteAsync(merchantOwner, $"""
            UPDATE public.shop_memberships SET authority_version = authority_version + 1
            WHERE shop_id = '{driftedShop.ShopId}' AND user_id = '{driftedShop.UserId}' AND role = 'Owner'
            """);

        await DrainAtAsync(f, EarlyStart, licensing: true);  // Consume -> Merchant refuses with expectedReason
        await DrainAtAsync(f, EarlyStart, licensing: false); // Refused receipt -> Licensing
        Assert.Equal(1L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM public.merchant_coverage_exception_decisions WHERE exception_id='{id}' AND phase='CoverageExceptionConsume' AND outcome='Refused' AND reason='{expectedReason}'"));
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals",
            $"exception_id='{id}' AND state='Refused' AND outcome_reason='{expectedReason}'"));
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exceptions",
            $"exception_id='{id}' AND state='Blocked' AND reason='{expectedReason}'"));
        await AssertZeroExceptionEffectsAsync(f, p.Account);
    }

    internal static async Task AssertOriginalRosterTradingAsync(P0308Fixture f, Package p, DateTimeOffset at)
    {
        f.At(at);
        var sessionClock = f.Hosts.Fixture.Clock;
        var original = sessionClock.Now;
        try
        {
            sessionClock.Now = at;
            f.Checkout.Delivery.Transport.LeaseClock.Now = at.AddSeconds(1);
            await f.Checkout.Delivery.ScanAsync(true);
            await f.Checkout.Delivery.DrainAsync(true);
            Assert.Equal("Allowed", await P0604Tests.FencedTradingAsync(f, p.Shops[0].ShopId));
            Assert.Equal("Allowed", await P0604Tests.FencedTradingAsync(f, p.Shops[1].ShopId));
            Assert.Equal("Allowed", await P0604Tests.FencedTradingAsync(f, p.Shops[2].ShopId));
            Assert.Equal("Licence.PendingActivation", await P0604Tests.FencedTradingAsync(f, p.Shops[3].ShopId));
        }
        finally { sessionClock.Now = original; }
    }

    private static object BuildScheduledReplacement(Guid outgoingShopId, Guid incomingShopId) => new
    {
        operationKey = Guid.NewGuid(),
        outgoingShopId,
        incomingShopId,
        observedEffectiveAt = February29,
        observedCapacity = 3,
        observedMemberAllowance = 5,
        observedPermitDeadline = (DateTimeOffset?)null,
        acknowledgedRenewal = true,
        acknowledgedPermitWait = true,
        acknowledgedShopSelection = true,
    };
}
