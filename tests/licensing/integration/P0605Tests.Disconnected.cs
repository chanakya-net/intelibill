using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0605Tests
{
    [Fact]
    public async Task UndeliveredGracePermitSurvivesTheDeliveryRetryHorizon()
    {
        using var key = RSA.Create(2048);
        var business = new P0201Clock { Now = January31 };
        await using var f = await PermitFixtureAsync(key, business);
        var checkout = f.Checkout;
        var p = await f.PackageAsync(4, ["Central", "East", "North", "South"], tradingIndex: 2);
        var (_, term) = await PurchaseAsync(p with { Shops = p.Shops[..3] }, "Month");
        await checkout.Delivery.DrainAsync(true);
        f.At(February14);
        var (id, _) = await ConsentAsync(f, p, term);
        var start = February29.AddMinutes(1);
        f.At(start);
        var auth = f.Hosts.Fixture.Clock;
        var originalAuthTime = auth.Now;
        auth.Now = start;
        checkout.Delivery.Transport.LeaseClock.Now = start;
        await checkout.Delivery.ScanAsync(true);
        await checkout.Delivery.DrainAsync(true);
        var north = p.Shops[2];
        await SignInAtPermitTimeAsync(north);
        var permit = await P0507Tests.Run.IssuePermitAsync(north, "North disconnected till");
        Assert.True(permit.ValidUntil > start.AddHours(1), permit.ValidUntil.ToString("O"));
        auth.Now = originalAuthTime;
        await LicensingPostgreSqlFixture.ExecuteAsync(checkout.Delivery.MerchantDb, $"""
            UPDATE entitlement_outbox o SET next_attempt_at='{permit.ValidUntil.AddDays(1):O}'
            FROM entitlement_messages m WHERE m.event_id=o.event_id AND m.kind='DeviceObservation' AND m.shop_id='{north.ShopId}'
            """);
        var (quote, _) = await RenewAsync(p, term, "Month");
        for (var step = 0; step <= 12; step++)
            await PumpAtAsync(f, start.AddMinutes(step * 5));
        Assert.Equal(0L, await f.CountAsync("offline_device_observations", $"shop_id='{north.ShopId}'"));
        var incoming = p.Shops[3].ShopId;
        Assert.Equal(0L, await f.CountAsync("entitlement_outbox o JOIN entitlement_messages m USING(event_id)", $"""
            o.shop_id='{incoming}' AND o.status='DeadLetter' AND convert_from(m.payload,'UTF8')::jsonb->'shop'->>'replacementId'='{id}'
            """));
        Assert.Equal("Applying", await ActivationAsync(p, term));
        Assert.Equal("Applying", (await P0302Fixture.StatusAsync(p.Payer.Client, quote)).GetProperty("access").GetString());
        await PumpAtAsync(f, permit.ValidUntil.AddMinutes(-1));
        Assert.Equal(0L, await ActivationCountAsync(f, id));
        auth.Now = permit.ValidUntil.AddTicks(-10);
        Assert.StartsWith("Licence.", await P0604Tests.FencedTradingAsync(f, incoming));
        auth.Now = originalAuthTime;
        await PumpAtAsync(f, permit.ValidUntil, deliverReceipts: false);
        var head = await checkout.Delivery.LatestSnapshotAsync(incoming);
        Assert.Equal(id, head.Shop!.ReplacementId);
        Assert.Equal(1L, await ActivationCountAsync(f, id));
        await PumpAtAsync(f, permit.ValidUntil.AddMinutes(1));
        Assert.Equal(1L, await f.CountAsync("entitlement_outbox", $"event_id='{head.EventId}' AND attempts>=2 AND status='Applied'"));
        Assert.Equal(1L, await ActivationCountAsync(f, id));
        Assert.Equal("Acknowledged", await ActivationAsync(p, term));
    }

    private async Task<P0308Fixture> PermitFixtureAsync(RSA key, P0201Clock business)
    {
        var checkout = await P0302Fixture.StartAsync(database, business, merchantExtras: new Dictionary<string, string?>
        {
            ["Jwt:AccessTokenExpiryMinutes"] = "360",
            ["OfflineLicensePermit:LocalKeyId"] = "replacement-test",
            ["OfflineLicensePermit:LocalPrivateKeyPem"] = key.ExportPkcs8PrivateKeyPem(),
        }, merchantServices: services => services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure<TimeProvider>((options, clock) => options.TokenValidationParameters.LifetimeValidator =
                (nbf, exp, _, _) => nbf <= clock.GetUtcNow().UtcDateTime && exp > clock.GetUtcNow().UtcDateTime));
        return new P0308Fixture(checkout);
    }

    private static async Task<string> ActivationAsync(Package p, Guid term)
    {
        var preview = await p.Payer.Client.GetFromJsonAsync<CoverageReplacementPreviewV1>(PathFor(p, term));
        return Assert.Single(preview!.Replacements).Activation;
    }

    private static Task<object?> ActivationCountAsync(P0308Fixture f, Guid id) =>
        f.Checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM coverage_replacement_activations WHERE replacement_id='{id}'");

    private static async Task PumpAtAsync(P0308Fixture f, DateTimeOffset at, bool deliverReceipts = true)
    {
        var delivery = f.Checkout.Delivery;
        var session = f.Hosts.Fixture.Clock;
        var original = session.Now;
        try
        {
            f.At(at);
            session.Now = at;
            delivery.Transport.LeaseClock.Now = at;
            await delivery.ScanAsync(true);
            await delivery.DrainAsync(true);
            if (deliverReceipts) await delivery.DrainAsync(false);
        }
        finally { session.Now = original; }
    }

    private static async Task SignInAtPermitTimeAsync(P0209Fixture.Merchant north)
    {
        north.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await P0201Tests.LoginAsync(north.Client, north.Email));
        using var switched = await north.Client.PostAsJsonAsync("/api/shops/switch", new { shopId = north.ShopId });
        switched.EnsureSuccessStatusCode();
        north.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await switched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
    }
}
