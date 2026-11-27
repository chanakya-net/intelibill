using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0418Tests
{
    [Fact]
    public async Task Enrolled_warm_sale_makes_no_outbound_http_request()
    {
        var outbound = new MerchantHttpCounter();
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() },
            merchantServices: services => services.AddSingleton<IHttpMessageHandlerBuilderFilter>(outbound), countAllTraffic: true);
        var (shop, _) = await PrepareShopAsync(fixture);
        using var client = shop.Client;
        var (service, _) = await AddServiceAsync(shop);
        using var verify = await client.GetAsync(AuthorityPath(shop.ShopId));
        Assert.True((await ReadAsync(verify, HttpStatusCode.OK)).GetProperty("enforced").GetBoolean());

        var before = (outbound.Calls, fixture.LicensingTraffic.Count);
        const string key = "p0418-local";
        using var first = await client.PostAsJsonAsync("/api/sales", SaleRequest(service, key));
        var body = await ReadAsync(first, HttpStatusCode.Created);
        await AssertReplayAsync(client, service, key, body);
        using var history = await client.GetAsync($"/api/sales/{body.GetProperty("saleId").GetGuid():D}");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Equal(before, (outbound.Calls, fixture.LicensingTraffic.Count));
    }

    [Fact]
    public async Task Cached_authority_ends_at_48_hours_and_replaying_the_signed_snapshot_does_not_renew_it()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow().AddHours(-48).AddMinutes(5) });
        var (shop, _) = await PrepareShopAsync(fixture);
        using var client = shop.Client;
        var (service, _) = await AddServiceAsync(shop);
        var snapshot = await fixture.LatestSnapshotAsync(shop.ShopId);
        using var read = await client.GetAsync(AuthorityPath(shop.ShopId));
        var verified = await ReadAsync(read, HttpStatusCode.OK);
        var end = verified.GetProperty("validUntil").GetDateTimeOffset();
        Assert.Equal(verified.GetProperty("authorityVerifiedAt").GetDateTimeOffset().AddHours(48), end);
        Assert.True(snapshot.Shop!.AccessEndsAt > end);

        fixture.Hosts.Fixture.Clock.Now = end.AddMicroseconds(-1);
        using var last = await client.PostAsJsonAsync("/api/sales", SaleRequest(service, "p0418-last-verified"));
        await ReadAsync(last, HttpStatusCode.Created);
        fixture.Hosts.Fixture.Clock.Now = end;

        var (http, acknowledgement) = await fixture.SendAsync(snapshot);
        using (http) Assert.True(http.IsSuccessStatusCode, await http.Content.ReadAsStringAsync());
        Assert.Equal("Applied", acknowledgement?.Outcome);
        using var stale = await client.PostAsJsonAsync("/api/sales", SaleRequest(service, "p0418-stale"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, stale.StatusCode);
        Assert.Equal("Licence.VerificationUnavailable", await P0206Fixture.CodeAsync(stale));
        Assert.Equal(1L, await CountSalesAsync(fixture, shop.ShopId));
    }

    private sealed class MerchantHttpCounter : IHttpMessageHandlerBuilderFilter
    {
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);

        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            builder.AdditionalHandlers.Insert(0, new CounterHandler(this));
        };

        private sealed class CounterHandler(MerchantHttpCounter counter) : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref counter._calls);
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
