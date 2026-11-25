using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0605Tests
{
    [Fact]
    public async Task UndeliveredGracePermitPreventsMerchantActivationUntilItsOwnDeadline()
    {
        using var key = RSA.Create(2048);
        var business = new P0201Clock { Now = January31 };
        var checkout = await P0302Fixture.StartAsync(database, business, merchantExtras: new Dictionary<string, string?>
        {
            ["OfflineLicensePermit:LocalKeyId"] = "replacement-test",
            ["OfflineLicensePermit:LocalPrivateKeyPem"] = key.ExportPkcs8PrivateKeyPem(),
        }, merchantServices: services => services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure<TimeProvider>((options, clock) => options.TokenValidationParameters.LifetimeValidator =
                (nbf, exp, _, _) => nbf <= clock.GetUtcNow().UtcDateTime && exp > clock.GetUtcNow().UtcDateTime));
        await using var f = new P0308Fixture(checkout);
        var p = await f.PackageAsync(4, ["Central", "East", "North", "South"], tradingIndex: 2);
        var (_, term) = await PurchaseAsync(p with { Shops = p.Shops[..3] }, "Month");
        await checkout.Delivery.DrainAsync(true);
        f.At(February14);
        var (id, _) = await ConsentAsync(f, p, term);
        f.At(February29.AddMinutes(1));
        var auth = f.Hosts.Fixture.Clock;
        var originalAuthTime = auth.Now;
        auth.Now = business.Now;
        checkout.Delivery.Transport.LeaseClock.Now = business.Now;
        await checkout.Delivery.ScanAsync(true);
        await checkout.Delivery.DrainAsync(true);
        var north = p.Shops[2];
        north.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await P0201Tests.LoginAsync(north.Client, north.Email));
        using var switched = await north.Client.PostAsJsonAsync("/api/shops/switch", new { shopId = north.ShopId });
        switched.EnsureSuccessStatusCode();
        north.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await switched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        var permit = await P0507Tests.Run.IssuePermitAsync(north, "North disconnected till");
        Assert.True(permit.ValidUntil > business.Now);
        auth.Now = originalAuthTime;
        Assert.Equal(0L, await f.CountAsync("offline_device_observations", $"shop_id='{north.ShopId}'"));
        await RenewAsync(p, term, "Month");
        await checkout.Delivery.DrainAsync(true);
        var incoming = await checkout.Delivery.LatestSnapshotAsync(p.Shops[3].ShopId);
        Assert.Equal(id, incoming.Shop!.ReplacementId);
        f.At(permit.ValidUntil.AddTicks(-10));
        var (busy, _) = await checkout.Delivery.SendAsync(incoming);
        using (busy) Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
        Assert.Equal(0L, await checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM coverage_replacement_activations WHERE replacement_id='{id}'"));
        f.At(permit.ValidUntil);
        var (success, ack) = await checkout.Delivery.SendAsync(incoming);
        using (success) Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal("Applied", ack!.Disposition);
        Assert.Equal(1L, await checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM coverage_replacement_activations WHERE replacement_id='{id}'"));
    }
}
