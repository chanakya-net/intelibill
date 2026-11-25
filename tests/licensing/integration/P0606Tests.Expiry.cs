using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0606Tests
{
    [Fact]
    public async Task ElapsedPaidCutoverBlocksRequestPreviewAndApproval()
    {
        using var key = RSA.Create(2048);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var (p, term) = await PrepareWithTradingNorthAsync(f);

        // Request with cutover at or before now returns Coverage.ExceptionInvalid (400).
        foreach (var badCutover in new[] { EarlyStart, EarlyStart.AddMinutes(-1) })
        {
            using var bad = await p.Payer.Client.PostAsJsonAsync(PathFor(p, term),
                BuildRequest(p, Guid.NewGuid(), badCutover, badCutover, "Wait", observedPermitDeadline: null));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal("Coverage.ExceptionInvalid", await P0206Fixture.CodeAsync(bad));
        }
        Assert.Equal(0L, await f.CountAsync("coverage_replacement_exceptions"));

        // Consented proposal whose cutover later elapses cannot be previewed as approvable or approved.
        var permit = await IssueRealPermitAsync(f, p.Shops[2], EarlyStart, deliverObservation: true);
        var cutover = permit.ValidUntil;
        var id = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, cutover, "CourtesyBridge", cutover);
        var (manager, _) = await f.Checkout.Trial.Catalogue.ManagerAsync("r002a.manager@example.test");
        using var _ = manager;
        var readyPreview = await AdminPreviewAsync(manager, p.Account, id);
        Assert.True(readyPreview.GetProperty("canApprove").GetBoolean());

        f.At(cutover);
        var elapsedPreview = await AdminPreviewAsync(manager, p.Account, id);
        Assert.False(elapsedPreview.GetProperty("canApprove").GetBoolean());
        Assert.Equal("Blocked", elapsedPreview.GetProperty("state").GetString());
        Assert.Contains("PaidCutoverElapsed",
            elapsedPreview.GetProperty("blockingReasons").EnumerateArray().Select(x => x.GetString()));

        using var staleApprove = await AdminApproveAsync(manager, p.Account, id, Guid.NewGuid(), readyPreview,
            "Attempt approval after cutover elapsed");
        Assert.Equal(HttpStatusCode.Conflict, staleApprove.StatusCode);
        await AssertZeroExceptionEffectsAsync(f, p.Account);

        // Ordinary renewal still works with the original roster {Central, East, North}.
        var quote = await RenewalQuoteAsync(p, term, "Month");
        Assert.Equal(CheckoutOrdersV1.ShopHash([p.Shops[0].ShopId, p.Shops[1].ShopId, p.Shops[2].ShopId]), quote.ShopHash);
    }

    [Fact]
    public async Task DelayedPrepareOrConsumePastPaidCutoverRefusesWithoutAllocating()
    {
        using var key = RSA.Create(2048);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var (p, term) = await PrepareWithTradingNorthAsync(f);
        var (manager, _) = await f.Checkout.Trial.Catalogue.ManagerAsync("r002b.manager@example.test");
        using var _ = manager;

        // (1) Approved before cutover, but Prepare is delivered after cutover -> Merchant refuses with PaidCutoverElapsed.
        var cutover1 = EarlyStart.AddHours(2);
        var id1 = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, cutover1, "CourtesyBridge", null);
        var preview1 = await AdminPreviewAsync(manager, p.Account, id1);
        using var approve1 = await AdminApproveAsync(manager, p.Account, id1, Guid.NewGuid(), preview1,
            "Approved before cutover; Prepare arrives late");
        Assert.Equal(HttpStatusCode.Created, approve1.StatusCode);

        var after1 = cutover1.AddMinutes(1);
        f.At(after1);
        await DrainAtAsync(f, after1, licensing: true);
        await DrainAtAsync(f, after1, licensing: false);
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals",
            $"exception_id='{id1}' AND state='Refused' AND outcome_reason='PaidCutoverElapsed'"));
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exceptions",
            $"exception_id='{id1}' AND state='Blocked' AND reason='PaidCutoverElapsed'"));
        await AssertZeroExceptionEffectsAsync(f, p.Account);

        // (2) Prepare completes before cutover2, Consume is queued, then clock passes cutover2 before Consume runs.
        var start2 = after1.AddMinutes(5);
        var cutover2 = start2.AddHours(2);
        f.At(start2);
        var id2 = await CreateConsentedExceptionAsync(f, p, term, start2, cutover2, "CourtesyBridge", null);
        var preview2 = await AdminPreviewAsync(manager, p.Account, id2);
        using var approve2 = await AdminApproveAsync(manager, p.Account, id2, Guid.NewGuid(), preview2,
            "Approved before cutover; Consume arrives late");
        Assert.Equal(HttpStatusCode.Created, approve2.StatusCode);

        await DrainAtAsync(f, start2, licensing: true);  // Prepare -> Prepared
        await DrainAtAsync(f, start2, licensing: false); // Prepared -> queues Consume
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals",
            $"exception_id='{id2}' AND state='Consuming'"));

        var after2 = cutover2.AddMinutes(1);
        f.At(after2);
        await DrainAtAsync(f, after2, licensing: true);  // Consume arrives after cutover -> Refused
        await DrainAtAsync(f, after2, licensing: false); // Refused receipt -> Licensing
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals",
            $"exception_id='{id2}' AND state='Refused' AND outcome_reason='PaidCutoverElapsed'"));
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exceptions",
            $"exception_id='{id2}' AND state='Blocked' AND reason='PaidCutoverElapsed'"));
        await AssertZeroExceptionEffectsAsync(f, p.Account);
        await AssertOriginalRosterTradingAsync(f, p, after2);
    }

    [Fact]
    public async Task CourtesyBridgeAppliedAfterRequestedEarlyStartBeginsAtGrantRecordedAt()
    {
        using var key = RSA.Create(2048);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var (p, term) = await PrepareWithTradingNorthAsync(f);
        var permit = await IssueRealPermitAsync(f, p.Shops[2], EarlyStart, deliverObservation: true);
        var cutover = permit.ValidUntil;

        // Requested early start is EarlyStart, but approval and consume commit 30 minutes later while still before cutover.
        var id = await CreateConsentedExceptionAsync(f, p, term, EarlyStart, cutover, "CourtesyBridge", cutover);
        var appliedAt = EarlyStart.AddMinutes(30);
        f.At(appliedAt);
        var (manager, _) = await f.Checkout.Trial.Catalogue.ManagerAsync("r002d.manager@example.test");
        using var _ = manager;
        var preview = await AdminPreviewAsync(manager, p.Account, id);
        Assert.True(preview.GetProperty("canApprove").GetBoolean());
        Assert.Equal(appliedAt, preview.GetProperty("courtesyStartsAt").GetDateTimeOffset());
        Assert.Equal(cutover, preview.GetProperty("courtesyEndsAt").GetDateTimeOffset());

        using var approved = await AdminApproveAsync(manager, p.Account, id, Guid.NewGuid(), preview,
            "Approved 30m after requested early start; courtesy begins at grant recorded_at");
        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);
        await DrainExceptionProtocolAsync(f, appliedAt);

        // Persisted courtesy grant starts at recorded_at (never before it) and ends at cutover.
        var grantJson = (string)(await f.ScalarAsync($"""
            SELECT jsonb_build_object('startsAt', g.starts_at, 'endsAt', g.ends_at, 'recordedAt', g.recorded_at,
                'handoffStartsAt', h.courtesy_starts_at)::text
            FROM public.paid_slot_handoffs h
            JOIN public.complimentary_grants g ON g.grant_id = h.courtesy_grant_id
            WHERE h.exception_id = '{id}'
            """))!;
        var grant = JsonSerializer.Deserialize<JsonElement>(grantJson);
        var startsAt = grant.GetProperty("startsAt").GetDateTimeOffset().ToUniversalTime();
        var recordedAt = grant.GetProperty("recordedAt").GetDateTimeOffset().ToUniversalTime();
        Assert.True(startsAt >= recordedAt, $"Courtesy startsAt {startsAt:O} preceded recordedAt {recordedAt:O}");
        Assert.Equal(appliedAt, startsAt);
        Assert.Equal(cutover, grant.GetProperty("endsAt").GetDateTimeOffset().ToUniversalTime());
        Assert.Equal(appliedAt, grant.GetProperty("handoffStartsAt").GetDateTimeOffset().ToUniversalTime());

        // Merchant activates the adjusted courtesy window cleanly and switches to Paid at cutover.
        Assert.Equal("Allowed", await FencedTradingAtAsync(f, p.Shops[3].ShopId, appliedAt));
        var afterApply = (await AdminPreviewAsync(manager, p.Account, id)).GetProperty("approval");
        Assert.Equal(appliedAt, afterApply.GetProperty("courtesyStartsAt").GetDateTimeOffset());
        Assert.Equal("Acknowledged", afterApply.GetProperty("courtesyActivation").GetString());
        await P0605Tests.VerifyTradingAsync(f, p, cutover, "Licence.Expired", "Allowed", deliverReceipts: true);
        await AssertSlotConservationOracleAsync(f, p.Account, term, capacity: 3, expectedStart: January31, expectedEnd: February29);
    }

    internal static async Task AssertZeroExceptionEffectsAsync(P0308Fixture f, Guid account)
    {
        Assert.Equal(0L, await f.CountAsync("paid_slot_handoffs", $"account_id='{account}'"));
        Assert.Equal(0L, await f.CountAsync("complimentary_grants", $"account_id='{account}'"));
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM public.merchant_coverage_exception_cutoffs WHERE account_id='{account}'"));
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM public.merchant_coverage_exception_activations WHERE account_id='{account}'"));
    }
}
