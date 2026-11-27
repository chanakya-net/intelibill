using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    private static readonly DateTimeOffset March31 = P0209Fixture.Utc("2028-03-31T04:30:00Z");
    private static readonly DateTimeOffset April8 = P0209Fixture.Utc("2028-04-08T04:30:00Z");
    private static readonly DateTimeOffset April10 = P0209Fixture.Utc("2028-04-10T04:30:00Z");
    private static readonly DateTimeOffset May10 = P0209Fixture.Utc("2028-05-10T04:30:00Z");

    [Fact]
    public async Task EffectiveShopStartPropagatesOnlyAcrossContiguousRenewalsAndPreservesPackageAnchor()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term1) = await PrepareAsync(fixture);
        var sibling = p.Shops[0];
        var joined = p.Shops[2];
        var delivery = fixture.Checkout.Delivery;

        await AcceptSlotAsync(p, term1, joined);
        await delivery.DrainAsync(false);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        // 1. At join instant (Feb 14): joined shop starts Feb 14, sibling & package start Jan 31, shared paidThrough Feb 29.
        var joinedT1 = await ShopTermAsync(p, joined);
        var siblingT1 = await ShopTermAsync(p, sibling);
        var pkgT1 = await AccountTermAsync(p, term1);
        Assert.Equal(February14, joinedT1.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(February29, joinedT1.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(January31, siblingT1.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(January31, pkgT1.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(February14, (await delivery.LatestSnapshotAsync(joined.ShopId)).Shop!.StartsAt);
        Assert.Equal(January31, (await delivery.LatestSnapshotAsync(sibling.ShopId)).Shop!.StartsAt);
        Assert.Equal(February14.UtcDateTime, (DateTime)(await delivery.MerchantScalarAsync(
            $"SELECT starts_at FROM shop_license_states WHERE shop_id='{joined.ShopId}'"))!);
        Assert.Equal(January31.UtcDateTime, (DateTime)(await delivery.MerchantScalarAsync(
            $"SELECT starts_at FROM shop_license_states WHERE shop_id='{sibling.ShopId}'"))!);

        // Verify subsequent P06-03 reduction preview still works using the shared package anchor on term1.
        var solo = await P0603Tests.PublishReductionPlanAsync(fixture, kind: "single-shop", name: "Solo Anchor Check",
            includedShops: 1, membersPerShop: 5, monthlyPricePaise: 6000L, extraShopsOffered: false);
        var reductionPreview = await P0603Tests.CreatePreviewAsync(p, term1, solo, 1, 3, 5, February29);
        Assert.Equal(January31, reductionPreview.CurrentStartsAt);
        Assert.Equal(February29, reductionPreview.CurrentPaidThrough);
        Assert.Equal(March31, reductionPreview.RenewalEndsAt);

        // 2. After early renewal (T2: [Feb 29, Mar 31)): joined keeps Feb 14, sibling keeps Jan 31, package T2 starts Feb 29.
        SyncAt(fixture, February14.AddHours(1));
        var (_, paid2) = await RenewAsync(p, term1, "Month");
        var term2 = paid2.GetProperty("termId").GetGuid();
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        var joinedT2 = await ShopTermAsync(p, joined);
        var siblingT2 = await ShopTermAsync(p, sibling);
        var pkgT2 = await AccountTermAsync(p, term2);
        Assert.Equal(February14, joinedT2.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(March31, joinedT2.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(January31, siblingT2.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(February29, pkgT2.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(February14, (await delivery.LatestSnapshotAsync(joined.ShopId)).Shop!.StartsAt);
        Assert.Equal(February14.UtcDateTime, (DateTime)(await delivery.MerchantScalarAsync(
            $"SELECT starts_at FROM shop_license_states WHERE shop_id='{joined.ShopId}'"))!);

        // 3. Lapse past T2 + grace (Apr 7) -> restore on Apr 10 (T3: [Apr 10, May 10)).
        SyncAt(fixture, April10);
        var restoredQuote = await AcceptAsync(p, await RenewalQuoteAsync(p, term2, "Month"));
        var paid3 = await PayAsync(p, restoredQuote);
        var term3 = paid3.GetProperty("termId").GetGuid();
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        var joinedT3 = await ShopTermAsync(p, joined);
        var siblingT3 = await ShopTermAsync(p, sibling);
        var pkgT3 = await AccountTermAsync(p, term3);
        Assert.Equal(April10, joinedT3.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(May10, joinedT3.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(April10, siblingT3.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(April10, pkgT3.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(April10, (await delivery.LatestSnapshotAsync(joined.ShopId)).Shop!.StartsAt);
        Assert.Equal(April10.UtcDateTime, (DateTime)(await delivery.MerchantScalarAsync(
            $"SELECT starts_at FROM shop_license_states WHERE shop_id='{joined.ShopId}'"))!);

        // 4. Historical re-evaluations with T1, T2, T3 all persisted:
        //    (a) Just before assignment (Feb 14 - 1us): joined has no term; sibling is Active on T1 starting Jan 31.
        SyncAt(fixture, February14.AddTicks(-10));
        Assert.Equal(JsonValueKind.Null, (await ShopTermAsync(p, joined)).ValueKind);
        var siblingBeforeJoin = await ShopTermAsync(p, sibling);
        Assert.Equal(term1, siblingBeforeJoin.GetProperty("termId").GetGuid());
        Assert.Equal(January31, siblingBeforeJoin.GetProperty("startsAt").GetDateTimeOffset());

        //    (b) Inside interrupted period (Apr 8, after T2 grace Apr 7 and before T3 Apr 10): both show T2 Expired with original starts.
        SyncAt(fixture, April8);
        var joinedGap = await ShopTermAsync(p, joined);
        var siblingGap = await ShopTermAsync(p, sibling);
        Assert.Equal(term2, joinedGap.GetProperty("termId").GetGuid());
        Assert.Equal("Expired", joinedGap.GetProperty("state").GetString());
        Assert.Equal(February14, joinedGap.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(term2, siblingGap.GetProperty("termId").GetGuid());
        Assert.Equal("Expired", siblingGap.GetProperty("state").GetString());
        Assert.Equal(January31, siblingGap.GetProperty("startsAt").GetDateTimeOffset());
    }
}
