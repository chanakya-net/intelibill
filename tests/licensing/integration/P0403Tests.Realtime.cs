using System.Text.Json;
using System.Threading.Channels;
using Intelibill.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0403Tests
{
    [Theory]
    [InlineData("Account", "Security", "/hubs/products", "ProductHub")]
    [InlineData("Shop", "Security", "/hubs/shop-updates", "ShopUpdatesHub")]
    [InlineData("Account", "Trading", "/hubs/shop-updates", "ShopUpdatesHub")]
    public async Task RealtimeAdmissionAndDeliveryRespectScopeAtTheEffectiveInstant(string scope, string mode,
        string path, string hub)
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var affected = await CreateTradingShopAsync(fixture.Hosts);
        var unaffected = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, affected);
        await LinkAsync(fixture, unaffected);
        await fixture.DrainAsync(source: true);
        await EnrollAsync(fixture, affected, unaffected);
        using var security = await SignInSecurityAsync(fixture);
        var effective = fixture.Hosts.Fixture.Clock.Now.AddSeconds(10);
        await ApplySuspensionAsync(security, account, scope, scope == "Shop" ? affected.ShopId : null, mode, effective);
        await fixture.DrainAsync(source: true);

        fixture.Hosts.Fixture.Clock.Now = effective.AddMicroseconds(-1);
        await using var existing = new RealtimeProbe(fixture, affected, path, hub);
        await existing.StartAsync();
        await using var unrelated = new RealtimeProbe(fixture, unaffected, path, hub);
        await unrelated.StartAsync();
        await WaitForLeasesAsync(fixture, hub, affected, 1);
        await WaitForLeasesAsync(fixture, hub, unaffected, 1);
        // An advisory from projection delivery can already be queued on this connection.
        if (hub == "ShopUpdatesHub")
        {
            await using var noticeScope = fixture.Hosts.Merchant.Services.CreateAsyncScope();
            await noticeScope.ServiceProvider.GetRequiredService<IShopUpdatesNotifier>()
                .NotifyShopUpdateAsync("LicenceAuthorityChanged", affected.ShopId, [], fixture.Hosts.Fixture.Clock.Now);
        }
        var before = Guid.NewGuid();
        await NotifyAsync(fixture, affected, hub, before);
        Assert.Equal(before, await existing.ReadChangeAsync());

        fixture.Hosts.Fixture.Clock.Now = effective;
        await using var fresh = new RealtimeProbe(fixture, affected, path, hub);
        await fresh.StartAsync();
        var after = Guid.NewGuid();
        var other = Guid.NewGuid();
        if (mode == "Security")
            await fresh.AssertClosedAsync();
        else
            await WaitForLeasesAsync(fixture, hub, affected, 2);
        await Task.WhenAll(NotifyAsync(fixture, affected, hub, after), NotifyAsync(fixture, unaffected, hub, other));
        Assert.Equal(other, await unrelated.ReadChangeAsync());

        if (mode == "Security")
        {
            using var noDelivery = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => existing.ReadChangeAsync(noDelivery.Token));
            await WaitForLeasesAsync(fixture, hub, affected, 1);
        }
        else
        {
            Assert.Equal(after, await existing.ReadChangeAsync());
            Assert.Equal(after, await fresh.ReadChangeAsync());
        }
    }

    private sealed class RealtimeProbe : IAsyncDisposable
    {
        private readonly HubConnection _connection;
        private readonly Channel<JsonElement> _updates = Channel.CreateUnbounded<JsonElement>();
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Guid _shopId;
        private readonly bool _products;

        internal RealtimeProbe(P0213Fixture fixture, Merchant shop, string path, string hub)
        {
            _shopId = shop.ShopId;
            _products = hub == "ProductHub";
            var client = fixture.Hosts.Merchant.Server.CreateWebSocketClient();
            client.ConfigureRequest = request => request.Headers.Authorization = shop.Client.DefaultRequestHeaders.Authorization!.ToString();
            _connection = new HubConnectionBuilder().WithUrl(new Uri($"https://localhost{path}"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, ct) => await client.ConnectAsync(context.Uri, ct);
            }).Build();
            _connection.On<JsonElement>(_products ? "ProductAdded" : "ShopUpdated",
                payload => _updates.Writer.TryWrite(payload));
            _connection.Closed += error =>
            {
                _updates.Writer.TryComplete(error);
                _closed.TrySetResult();
                return Task.CompletedTask;
            };
        }

        internal async Task StartAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _connection.StartAsync(timeout.Token);
        }

        internal async Task<Guid> ReadChangeAsync(CancellationToken ct = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (true)
            {
                var payload = await _updates.Reader.ReadAsync(timeout.Token);
                Assert.Equal(_shopId, payload.GetProperty("shopId").GetGuid());
                if (_products) return payload.GetProperty("itemId").GetGuid();

                var eventType = payload.GetProperty("eventType").GetString();
                var ids = payload.GetProperty("changedIds").EnumerateArray();
                if (eventType == "LicenceAuthorityChanged")
                {
                    Assert.Empty(ids);
                    continue;
                }
                Assert.Equal("fixture", eventType);
                return Assert.Single(ids).GetGuid();
            }
        }

        internal async Task AssertClosedAsync()
        {
            await _closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(_updates.Reader.TryRead(out _), "The refused connection received a notification.");
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private static async Task WaitForLeasesAsync(P0213Fixture fixture, string hub, Merchant shop, int count)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var actual = await fixture.MerchantScalarAsync($"""
                SELECT count(*) FROM realtime_connection_leases WHERE hub_name = '{hub}' AND shop_id = '{shop.ShopId:D}'
                """);
            if (Equals((long)count, actual)) return;
            await Task.Delay(25);
        }
        Assert.Fail($"Expected {count} leases on {hub} for shop {shop.ShopId:D}.");
    }

    private static async Task NotifyAsync(P0213Fixture fixture, Merchant shop, string hub, Guid id)
    {
        await using var service = fixture.Hosts.Merchant.Services.CreateAsyncScope();
        if (hub == "ProductHub")
        {
            await service.ServiceProvider.GetRequiredService<IProductHubNotifier>()
                .NotifyProductCreatedAsync(id, "fixture", "Notification fixture item", shop.ShopId);
        }
        else
        {
            await service.ServiceProvider.GetRequiredService<IShopUpdatesNotifier>()
                .NotifyShopUpdateAsync("fixture", shop.ShopId, [id], fixture.Hosts.Fixture.Clock.Now);
        }
    }
}
