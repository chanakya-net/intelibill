using System.Net.WebSockets;
using System.Text;
using Intelibill.Application.Common.Interfaces;
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
        using var existing = await ConnectRealtimeAsync(fixture, affected, path);
        using var unrelated = await ConnectRealtimeAsync(fixture, unaffected, path);
        await WaitForLeasesAsync(fixture, hub, affected, 1);
        await WaitForLeasesAsync(fixture, hub, unaffected, 1);
        var before = Guid.NewGuid();
        await NotifyAsync(fixture, affected, hub, before);
        Assert.Contains(before.ToString(), await ReadFrameAsync(existing));

        fixture.Hosts.Fixture.Clock.Now = effective;
        using var fresh = await ConnectRealtimeAsync(fixture, affected, path);
        var after = Guid.NewGuid();
        var other = Guid.NewGuid();
        if (mode == "Security")
            await AssertClosedAsync(fresh);
        else
            await WaitForLeasesAsync(fixture, hub, affected, 2);
        await Task.WhenAll(NotifyAsync(fixture, affected, hub, after), NotifyAsync(fixture, unaffected, hub, other));
        Assert.Contains(other.ToString(), await ReadFrameAsync(unrelated));

        if (mode == "Security")
        {
            using var noDelivery = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            var received = await Record.ExceptionAsync(() => ReadFrameAsync(existing, noDelivery.Token));
            Assert.IsAssignableFrom<OperationCanceledException>(received);
            await WaitForLeasesAsync(fixture, hub, affected, 1);
        }
        else
        {
            Assert.Contains(after.ToString(), await ReadFrameAsync(existing));
            Assert.Contains(after.ToString(), await ReadFrameAsync(fresh));
        }
    }

    private static async Task<WebSocket> ConnectRealtimeAsync(P0213Fixture fixture, Merchant shop, string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = fixture.Hosts.Merchant.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = shop.Client.DefaultRequestHeaders.Authorization!.ToString();
        var socket = await client.ConnectAsync(new Uri($"wss://localhost{path}"), timeout.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),
            WebSocketMessageType.Text, true, timeout.Token);
        return socket;
    }

    private static async Task<string> ReadFrameAsync(WebSocket socket, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var bytes = new byte[4096];
        while (true)
        {
            var frame = await socket.ReceiveAsync(bytes.AsMemory(), timeout.Token);
            if (frame.MessageType == WebSocketMessageType.Close) return "";
            var value = Encoding.UTF8.GetString(bytes, 0, frame.Count);
            if (value.Contains("\"target\"", StringComparison.Ordinal)) return value;
        }
    }

    private static async Task AssertClosedAsync(WebSocket socket)
    {
        try
        {
            Assert.Empty(await ReadFrameAsync(socket));
        }
        catch (WebSocketException)
        {
            // TestServer may abort the WebSocket immediately after completing the SignalR handshake.
        }
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
