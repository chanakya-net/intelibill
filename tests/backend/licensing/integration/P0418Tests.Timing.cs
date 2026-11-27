using System.Diagnostics;
using System.Net;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0418TimingTests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task Healthy_restriction_is_enforced_and_acknowledged_within_the_target()
    {
        await using var fixture = await P0213Fixture.StartAsync(database, browser: true, prepareProvider: true,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() }, liveDelivery: true);
        using var admin = await P0403Tests.SignInSecurityAsync(fixture);
        var samples = new List<Observation>();
        for (var sample = 0; sample < 5; sample++)
        {
            var shop = await CreateTradingShopAsync(fixture.Hosts);
            using var client = shop.Client;
            var account = await P0403Tests.LinkAsync(fixture, shop);
            await WaitForProjectionAsync(fixture, shop.ShopId);
            await P0403Tests.EnrollAsync(fixture, shop);
            using var operating = await client.GetAsync(P0418Tests.AuthorityPath(shop.ShopId));
            Assert.True(operating.IsSuccessStatusCode, await operating.Content.ReadAsStringAsync());

            var path = $"/api/admin/accounts/{account:D}/restrictions";
            var id = await P0404Tests.SuspendAsync(admin, path, "Shop", shop.ShopId, "Trading");
            samples.Add(await MeasureAsync(admin, shop, $"{path}/{id:D}", Stopwatch.StartNew()));
        }

        Assert.Contains(samples, sample => sample.PendingObserved);
        if (Environment.GetEnvironmentVariable("P0418_EVIDENCE_DIR") is { Length: > 0 } directory)
            await RecordAsync(directory, samples);
    }

    private static async Task WaitForProjectionAsync(P0213Fixture fixture, Guid shop)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!Equals("Active", await fixture.MerchantScalarAsync(
            $"SELECT state FROM shop_license_states WHERE shop_id = '{shop:D}'")))
            await Task.Delay(50, timeout.Token);
    }

    private static async Task<Observation> MeasureAsync(P0110CookieBrowser admin, Merchant shop, string path, Stopwatch watch)
    {
        long? enforced = null;
        var sawPending = false;
        while (watch.Elapsed < TimeSpan.FromSeconds(60))
        {
            using var status = await admin.SendAsync(HttpMethod.Get, path);
            var body = await P0418Tests.ReadAsync(status, HttpStatusCode.OK);
            var propagation = body.GetProperty("propagation").GetString();
            sawPending |= propagation == "Pending";
            var acknowledged = propagation == "Acknowledged" ? watch.ElapsedMilliseconds : (long?)null;
            using var authority = await shop.Client.GetAsync(P0418Tests.AuthorityPath(shop.ShopId));
            var denied = authority.StatusCode == HttpStatusCode.Forbidden
                && await P0206Fixture.CodeAsync(authority) == "Licence.TradingRestricted";
            if (denied) enforced ??= watch.ElapsedMilliseconds;

            if (acknowledged is { } elapsed)
            {
                Assert.True(denied, "An acknowledged restriction must already refuse merchant trading.");
                Assert.InRange(enforced!.Value, 0, 59_999);
                return new(shop.ShopId, enforced.Value, elapsed, sawPending);
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Restriction {path} did not acknowledge within 60 seconds.");
    }

    private static Task RecordAsync(string directory, List<Observation> samples)
    {
        var ordered = samples.Select(sample => sample.AcknowledgedMs).Order().ToArray();
        var destination = Path.Combine(directory, "timing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        return File.WriteAllTextAsync(Path.Combine(destination, "acknowledgement-timing.json"), JsonSerializer.Serialize(new
        {
            provenance = "Local loopback Kestrel merchant and licensing APIs, one PostgreSQL container, fixture keys",
            workerTickMs = 2000, batch = 10, pollMs = 50,
            samples, minimumMs = ordered[0], medianMs = ordered[ordered.Length / 2], maximumMs = ordered[^1],
        }));
    }

    private sealed record Observation(Guid ShopId, long EnforcedMs, long AcknowledgedMs, bool PendingObserved);
}
