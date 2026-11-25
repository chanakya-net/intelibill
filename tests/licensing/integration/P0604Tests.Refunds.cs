using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    internal static readonly DateTimeOffset February20 = P0209Fixture.Utc("2028-02-20T04:30:00Z");
    private static readonly string[] InvalidPendingStates = ["Stale", "Expired"];

    internal static void SyncAt(P0308Fixture fixture, DateTimeOffset at) => fixture.At(at);

    internal static async Task<(Package Package, Guid OrderId, Guid Term)> PrepareRefundableAsync(P0308Fixture fixture)
    {
        var package = await fixture.PackageAsync(4, ["Central", "East", "North", "South"]);
        var (purchase, term) = await PurchaseAsync(package with { Shops = package.Shops[..2] }, "Month");
        await fixture.Checkout.Delivery.DrainAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(false);
        SyncAt(fixture, February14);
        var adminClock = fixture.Checkout.Trial.Catalogue.Clock;
        P0609Tests.SetCredentialTime(fixture, adminClock.GetUtcNow());
        adminClock.Advanced += now => P0609Tests.SetCredentialTime(fixture, now);
        return (package, purchase.OrderId, term);
    }

    internal static async Task ApproveRefundAsync(P0308Fixture fixture, Package package, Guid orderId,
        string treatment, DateTimeOffset? effectiveAt = null, long paise = 3000L, P0110CookieBrowser? billingOperator = null)
    {
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, orderId);
        var refundRequest = await P0609Tests.RequestRefundAsync(package, orderId, confirmation, paise);
        using var ownedBilling = billingOperator is null ? await P0306Tests.SignInBillingAsync(fixture.Checkout) : null;
        var billing = billingOperator ?? ownedBilling!;
        P0609Tests.SetCredentialTime(fixture, fixture.Checkout.Trial.Catalogue.Clock.GetUtcNow());
        var preview = await P0609Tests.PreviewAsync(billing, package.Account, refundRequest, paise, treatment, effectiveAt);
        await P0609Tests.ReadAsync(await P0609Tests.ApproveAsync(billing, package.Account, refundRequest,
            P0609Tests.Command(preview, paise, treatment, effectiveAt)), HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("EndAt", 0)]
    [InlineData("TermAdjustment", 7)]
    [InlineData("Preserve", 7)]
    public async Task RefundEffectivePaidThroughControlsPreviewBoundariesAndReads(string treatment, int graceDays)
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, orderId, term) = await PrepareRefundableAsync(fixture);
        var expectedPaid = treatment == "Preserve" ? February29 : February20;
        var expectedAccess = expectedPaid.AddDays(graceDays);
        await ApproveRefundAsync(fixture, p, orderId, treatment, treatment == "Preserve" ? null : February20);
        await fixture.Checkout.Delivery.DrainAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(false);

        var path = Slots(p, term);
        var preview = await p.Payer.Client.GetFromJsonAsync<CoverageSlotTermV1>(path);
        Assert.Equal(expectedPaid, preview!.PaidThrough);
        Assert.Equal(1, preview.UnusedSlots);

        if (treatment != "Preserve")
        {
            using var stale = await p.Payer.Client.PostAsJsonAsync(path, Request(p.Shops[2].ShopId, Guid.NewGuid()));
            Assert.Equal("Coverage.SlotSourceChanged", await P0206Fixture.CodeAsync(stale));
            Assert.Equal(0L, await fixture.CountAsync("coverage_slot_assignments"));
            Assert.Equal(2L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
        }

        // Exact paid-through (and grace end) refuse new slot assignment with zero links and zero new money facts.
        SyncAt(fixture, expectedPaid);
        using var atPaid = await p.Payer.Client.PostAsJsonAsync(path, new
        {
            operationKey = Guid.NewGuid(), shopId = p.Shops[3].ShopId, observedCapacity = 3,
            observedPaidThrough = expectedPaid, acknowledgedSharedEnd = true, acknowledgedNoNewFee = true,
        });
        Assert.Equal("Coverage.SlotUnavailable", await P0206Fixture.CodeAsync(atPaid));

        if (graceDays > 0)
        {
            SyncAt(fixture, expectedAccess);
            using var atGrace = await p.Payer.Client.PostAsJsonAsync(path, new
            {
                operationKey = Guid.NewGuid(), shopId = p.Shops[3].ShopId, observedCapacity = 3,
                observedPaidThrough = expectedPaid, acknowledgedSharedEnd = true, acknowledgedNoNewFee = true,
            });
            Assert.Equal("Coverage.SlotUnavailable", await P0206Fixture.CodeAsync(atGrace));
        }

        // One microsecond before effective paid-through succeeds, freezes effective paid-through, and keeps paid vs grace distinct.
        SyncAt(fixture, expectedPaid.AddTicks(-10));
        var key = Guid.NewGuid();
        using var created = await p.Payer.Client.PostAsJsonAsync(path, new
        {
            operationKey = key, shopId = p.Shops[2].ShopId, observedCapacity = 3,
            observedPaidThrough = expectedPaid, acknowledgedSharedEnd = true, acknowledgedNoNewFee = true,
        });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var row = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedPaid, row.GetProperty("coverageEndsAt").GetDateTimeOffset());
        Assert.True(row.GetProperty("expiresAt").GetDateTimeOffset() <= expectedPaid);

        var id = row.GetProperty("assignmentId").GetGuid();
        var consentPath = Consent(p, p.Shops[2].ShopId, id);
        (await Decide(p.Shops[2].Client, consentPath, "grant")).EnsureSuccessStatusCode();
        (await Decide(p.Shops[2].Client, consentPath, "accept")).EnsureSuccessStatusCode();
        await fixture.Checkout.Delivery.DrainAsync(false);
        await fixture.Checkout.Delivery.DrainAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(false);

        Assert.Equal(3L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
        Assert.Equal(1L, await fixture.CountAsync("verified_payments"));
        var shopStatus = await ShopTermAsync(p, p.Shops[2]);
        var acctStatus = await AccountTermAsync(p, term);
        Assert.Equal(expectedPaid, shopStatus.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(expectedPaid, acctStatus.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(expectedAccess, shopStatus.GetProperty("accessEndsAt").GetDateTimeOffset());
        Assert.Equal(expectedAccess, acctStatus.GetProperty("accessEndsAt").GetDateTimeOffset());
        Assert.Equal(expectedAccess, (DateTime)(await fixture.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT access_ends_at FROM shop_license_states WHERE shop_id='{p.Shops[2].ShopId}'"))!);

        // Exact redelivery of the consent receipt is idempotent.
        var bytes = (byte[])(await fixture.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT payload FROM entitlement_messages WHERE shop_id='{p.Shops[2].ShopId}' AND kind='CoverageSlotConsent'"))!;
        using var replay = await DeliverConsentAsync(fixture.Checkout.Delivery, bytes);
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.Equal(3L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
    }

    [Theory]
    [InlineData("EndAt", true)]
    [InlineData("TermAdjustment", true)]
    [InlineData("EndAt", false)]
    [InlineData("TermAdjustment", false)]
    public async Task RefundDuringPendingConsentInvalidatesAcceptOrRefusesDeliveryWithoutLinks(string treatment, bool beforeAccept)
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, orderId, term) = await PrepareRefundableAsync(fixture);
        var shop = p.Shops[2];
        var key = Guid.NewGuid();
        var path = Slots(p, term);
        using var created = await p.Payer.Client.PostAsJsonAsync(path, Request(shop.ShopId, key));
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assignmentId").GetGuid();
        var consentPath = Consent(p, shop.ShopId, id);
        (await Decide(shop.Client, consentPath, "grant")).EnsureSuccessStatusCode();

        if (!beforeAccept)
            (await Decide(shop.Client, consentPath, "accept")).EnsureSuccessStatusCode();

        await ApproveRefundAsync(fixture, p, orderId, treatment, February20);

        if (beforeAccept)
        {
            var listed = await p.Payer.Client.GetFromJsonAsync<JsonElement>(path);
            var listState = listed.GetProperty("assignments")[0].GetProperty("state").GetString();
            Assert.Contains(listState, InvalidPendingStates);
            var recovered = await p.Payer.Client.GetFromJsonAsync<JsonElement>($"{path}/operations/{key}");
            Assert.Equal(listState, recovered.GetProperty("state").GetString());
            var ownerView = await shop.Client.GetFromJsonAsync<JsonElement>(consentPath);
            Assert.Contains(ownerView.GetProperty("status").GetString(), InvalidPendingStates);
            using var rejected = await Decide(shop.Client, consentPath, "accept");
            Assert.Equal("Billing.ConsentInvalid", await P0206Fixture.CodeAsync(rejected));
            Assert.Equal(0L, await fixture.Checkout.Delivery.MerchantScalarAsync(
                $"SELECT count(*) FROM entitlement_messages WHERE shop_id='{shop.ShopId}' AND kind='CoverageSlotConsent'"));
        }
        else
        {
            var bytes = (byte[])(await fixture.Checkout.Delivery.MerchantScalarAsync(
                $"SELECT payload FROM entitlement_messages WHERE shop_id='{shop.ShopId}' AND kind='CoverageSlotConsent'"))!;
            await fixture.Checkout.Delivery.DrainAsync(false);
            Assert.Equal("Refused", await fixture.ScalarAsync($"SELECT state FROM coverage_slot_assignments WHERE assignment_id='{id}'"));
            // Original accepted deadline evidence is preserved intact.
            Assert.Equal(February29.UtcDateTime, (DateTime)(await fixture.ScalarAsync(
                $"SELECT coverage_ends_at FROM coverage_slot_assignments WHERE assignment_id='{id}'"))!);
            using var redelivery = await DeliverConsentAsync(fixture.Checkout.Delivery, bytes);
            Assert.Equal(HttpStatusCode.NoContent, redelivery.StatusCode);
        }

        Assert.Equal(2L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
        Assert.Equal(1L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.CountAsync("coverage_slot_assignment_deliveries"));
    }
}
