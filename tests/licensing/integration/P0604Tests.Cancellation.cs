using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    [Fact]
    public async Task ScheduledCancellationBlocksSlotRequestAndDelayedReceiptUntilWithdrawn()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term1) = await PrepareAsync(fixture);
        // Renew into a currently active, fulfilment-backed renewable term T2 ([Feb 29, Mar 31)) with 1 spare slot.
        var (_, paid2) = await RenewAsync(p, term1, "Month");
        var term2 = paid2.GetProperty("termId").GetGuid();
        var end2 = paid2.GetProperty("termEndsAt").GetDateTimeOffset();
        SyncAt(fixture, February29.AddDays(1));
        await fixture.Checkout.Delivery.DrainAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(false);

        var slotsPath = Slots(p, term2);
        var cancelPath = P0215Tests.CancellationPath(p.Account, term2);
        var withdrawPath = P0216Tests.WithdrawalPath(p.Account, term2);

        // 1. Schedule cancellation BEFORE requesting a slot -> 0 unused slots, request refused, zero links.
        var scheduled1 = await P0216Tests.ScheduleAsync(p.Payer, p.Account, term2);
        var previewBlocked = await p.Payer.Client.GetFromJsonAsync<CoverageSlotTermV1>(slotsPath);
        Assert.Equal(0, previewBlocked!.UnusedSlots);
        using var refusedBefore = await p.Payer.Client.PostAsJsonAsync(slotsPath, new
        {
            operationKey = Guid.NewGuid(), shopId = p.Shops[2].ShopId, observedCapacity = 3,
            observedPaidThrough = end2, acknowledgedSharedEnd = true, acknowledgedNoNewFee = true,
        });
        Assert.Equal("Coverage.SlotUnavailable", await P0206Fixture.CodeAsync(refusedBefore));
        Assert.Equal(2L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term2}'"));

        // Withdraw cancellation -> slot becomes assignable again.
        using var w1 = await p.Payer.Client.PostAsJsonAsync(withdrawPath, P0216Tests.Withdraw(scheduled1, Guid.NewGuid()));
        w1.EnsureSuccessStatusCode();

        // 2. Request slot and accept on merchant, then schedule cancellation BEFORE receipt processing.
        using var reqResp = await p.Payer.Client.PostAsJsonAsync(slotsPath, new
        {
            operationKey = Guid.NewGuid(), shopId = p.Shops[2].ShopId, observedCapacity = 3,
            observedPaidThrough = end2, acknowledgedSharedEnd = true, acknowledgedNoNewFee = true,
        });
        Assert.True(reqResp.IsSuccessStatusCode, await reqResp.Content.ReadAsStringAsync());
        var firstAssignment = (await reqResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assignmentId").GetGuid();
        var consentPath = Consent(p, p.Shops[2].ShopId, firstAssignment);
        (await Decide(p.Shops[2].Client, consentPath, "grant")).EnsureSuccessStatusCode();
        (await Decide(p.Shops[2].Client, consentPath, "accept")).EnsureSuccessStatusCode();

        var scheduled2 = await P0216Tests.ScheduleAsync(p.Payer, p.Account, term2);
        await fixture.Checkout.Delivery.DrainAsync(false);
        Assert.Equal("Refused", await fixture.ScalarAsync(
            $"SELECT state FROM coverage_slot_assignments WHERE assignment_id='{firstAssignment}'"));
        Assert.Equal(2L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term2}'"));
        Assert.Equal(0L, await fixture.CountAsync("coverage_slot_assignment_deliveries"));

        // Frozen cancellation scope names only the 2 original shops, and shop + account readers agree on no grace at end2.
        var frozen = await P0215Fixture.PreviewAsync(p.Payer, cancelPath);
        Assert.Equal(p.Shops[..2].Select(s => s.ShopId).Order(),
            frozen.GetProperty("affectedShops").EnumerateArray().Select(s => s.GetProperty("shopId").GetGuid()).Order());
        var acctStatus = await AccountTermAsync(p, term2);
        var shopStatus = await ShopTermAsync(p, p.Shops[0]);
        Assert.Equal(end2, acctStatus.GetProperty("accessEndsAt").GetDateTimeOffset());
        Assert.Equal(end2, shopStatus.GetProperty("accessEndsAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, acctStatus.GetProperty("graceEndsAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, shopStatus.GetProperty("graceEndsAt").ValueKind);

        // 3. Withdraw cancellation -> a subsequent FRESH assignment succeeds and activates cleanly.
        using var w2 = await p.Payer.Client.PostAsJsonAsync(withdrawPath, P0216Tests.Withdraw(scheduled2, Guid.NewGuid()));
        w2.EnsureSuccessStatusCode();

        using var freshResp = await p.Payer.Client.PostAsJsonAsync(slotsPath, new
        {
            operationKey = Guid.NewGuid(), shopId = p.Shops[2].ShopId, observedCapacity = 3,
            observedPaidThrough = end2, acknowledgedSharedEnd = true, acknowledgedNoNewFee = true,
        });
        Assert.True(freshResp.IsSuccessStatusCode, await freshResp.Content.ReadAsStringAsync());
        var freshId = (await freshResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assignmentId").GetGuid();
        var freshConsent = Consent(p, p.Shops[2].ShopId, freshId);
        (await Decide(p.Shops[2].Client, freshConsent, "grant")).EnsureSuccessStatusCode();
        (await Decide(p.Shops[2].Client, freshConsent, "accept")).EnsureSuccessStatusCode();
        await fixture.Checkout.Delivery.DrainAsync(false);
        await fixture.Checkout.Delivery.DrainAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(false);

        Assert.Equal("Applied", await fixture.ScalarAsync($"SELECT state FROM coverage_slot_assignments WHERE assignment_id='{freshId}'"));
        Assert.Equal(3L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term2}'"));
        Assert.Equal(end2.AddDays(7), (await ShopTermAsync(p, p.Shops[2])).GetProperty("accessEndsAt").GetDateTimeOffset());
    }
}
