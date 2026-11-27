using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0605Tests
{
    internal static async Task ObservePermitAsync(P0308Fixture f, Package p, DateTimeOffset start, DateTimeOffset end)
    {
        var shop = p.Shops[2].ShopId;
        var affiliation = (Guid)(await f.ScalarAsync($"SELECT request_id FROM shop_affiliations WHERE shop_id='{shop}'"))!;
        var accountRevision = (long)(await f.ScalarAsync($"SELECT max(account_revision) FROM entitlement_snapshots WHERE shop_id='{shop}'"))!;
        var shopRevision = (long)(await f.ScalarAsync($"SELECT max(shop_revision) FROM entitlement_snapshots WHERE shop_id='{shop}'"))!;
        var window = new OfflineDevicePermitWindowV1(Guid.NewGuid(), start, end, OfflineDeviceObservationsV1.VerificationFreshness,
            accountRevision, shopRevision, start);
        var observation = new OfflineDeviceObservationV1(1, Guid.NewGuid(), p.Account, shop, affiliation,
            OfflineDeviceObservationsV1.DeviceRef(shop, "North till"), OfflineDeviceObservationsV1.PermitIssued, start, window);
        var delivery = f.Checkout.Delivery;
        using var signed = await delivery.Transport.SignedAsync(JsonSerializer.SerializeToUtf8Bytes(observation, EntitlementSnapshotsV1.Json),
            delivery.AuthClock, merchant: true, path: OfflineDeviceObservationsV1.Path);
        using var response = await delivery.LicensingApi.SendAsync(signed);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task NewlyObservedPermissionInvalidatesTheOwnersEarlierPreview()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var preview = await p.Payer.Client.GetFromJsonAsync<CoverageReplacementPreviewV1>(PathFor(p, term));
        Assert.Null(Assert.Single(preview!.CoveredShops, shop => shop.ShopId == p.Shops[2].ShopId).PermitDeadline);
        await ObservePermitAsync(f, p, February14, February14.AddDays(2));
        using var stale = await p.Payer.Client.PostAsJsonAsync(PathFor(p, term), Request(p, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(0L, await f.CountAsync("coverage_replacements"));
    }

    [Fact]
    public async Task PermitReportedAfterSchedulingDefersSuccessorUntilExactExpiry()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var (id, _) = await ConsentAsync(f, p, term);
        f.At(February29.AddHours(-1));
        var end = February29.AddHours(12);
        await ObservePermitAsync(f, p, February29.AddHours(-1), end);
        await RenewAsync(p, term, "Month");
        f.At(February29);
        await f.Checkout.Delivery.ScanAsync(true);
        await f.Checkout.Delivery.DrainAsync(true);
        await f.Checkout.Delivery.DrainAsync(false);
        var row = await p.Payer.Client.GetFromJsonAsync<JsonElement>(PathFor(p, term));
        Assert.Equal("Waiting", Assert.Single(row.GetProperty("replacements").EnumerateArray()).GetProperty("activation").GetString());
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM coverage_replacement_activations WHERE replacement_id='{id}'"));
        f.At(end);
        await f.Checkout.Delivery.ScanAsync(true);
        await f.Checkout.Delivery.DrainAsync(true);
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(1L, await f.Checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM coverage_replacement_activations WHERE replacement_id='{id}'"));
    }
}
