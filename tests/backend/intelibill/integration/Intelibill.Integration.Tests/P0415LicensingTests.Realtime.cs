using System.Security.Cryptography;
using Intelibill.Api.Hubs;
using Intelibill.Api.Services.Realtime;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0415LicensingTests
{
    [Theory]
    [InlineData("products")]
    [InlineData("shop-updates")]
    public async Task Applied_security_stops_realtime_for_the_affected_shop(string hub)
    {
        using var key = RSA.Create(2048);
        using var host = CreateHost(key);
        using var replica = CreateHost(key);
        await host.InitializeAsync();
        using var merchant = CreateClient(host);
        using var unrelated = CreateClient(host);
        using var delivery = CreateClient(host);
        var shop = await CreateShopAsync(merchant);
        var otherShop = await CreateShopAsync(unrelated);
        var grant = await BindAsync(shop);
        await DeliverAsync(delivery, key, grant);
        await EnforceAsync(shop);

        await using var affectedConnection = Connection(replica, merchant, hub);
        await using var unrelatedConnection = Connection(replica, unrelated, hub);
        var admitted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var affectedAfter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unrelatedAfter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var affectedSubscription = affectedConnection.On<string>("HistoryFixture", msg =>
        {
            if (msg == "before") admitted.TrySetResult(msg);
            else affectedAfter.TrySetResult(msg);
        });
        using var unrelatedSubscription = unrelatedConnection.On<string>("HistoryFixture", msg => unrelatedAfter.TrySetResult(msg));
        await affectedConnection.StartAsync(Ct);
        await unrelatedConnection.StartAsync(Ct);
        await WaitForLeaseAsync(replica, affectedConnection.ConnectionId!);
        await WaitForLeaseAsync(replica, unrelatedConnection.ConnectionId!);
        await SendFixtureEventAsync(replica, hub, shop, "before");
        Assert.Equal("before", await admitted.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        await DeliverAsync(delivery, key, grant with
        {
            EventId = Guid.NewGuid(),
            Account = grant.Account! with { Revision = 2, Restriction = "Security" },
        });
        await SendFixtureEventAsync(replica, hub, shop, "affected");
        await SendFixtureEventAsync(replica, hub, otherShop, "unrelated");
        Assert.Equal("unrelated", await unrelatedAfter.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<TimeoutException>(() => affectedAfter.Task.WaitAsync(TimeSpan.FromMilliseconds(400)));

        await using var refused = Connection(replica, merchant, hub);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        refused.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        try
        {
            await refused.StartAsync(deadline.Token);
            await closed.Task.WaitAsync(deadline.Token);
        }
        catch (HttpRequestException)
        {
            // Long polling may observe refusal as the connection closes during its initial handshake.
        }
    }

    private static HubConnection Connection(ApiWebApplicationFactory host, HttpClient client, string hub) =>
        new HubConnectionBuilder().WithUrl(new Uri($"https://localhost/hubs/{hub}"), options =>
        {
            options.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
            options.AccessTokenProvider = () => Task.FromResult(client.DefaultRequestHeaders.Authorization!.Parameter);
        }).Build();

    private static async Task WaitForLeaseAsync(ApiWebApplicationFactory host, string connectionId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await db.RealtimeConnectionLeases.AnyAsync(row => row.ConnectionId == connectionId, deadline.Token))
            await Task.Delay(20, deadline.Token);
    }

    private static async Task SendFixtureEventAsync(ApiWebApplicationFactory host, string hub, Guid shop, string message)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (hub == "products")
            await SendAsync<ProductHub>(services, shop, message);
        else
            await SendAsync<ShopUpdatesHub>(services, shop, message);
    }

    private static Task SendAsync<THub>(IServiceProvider services, Guid shop, string message) where THub : Hub =>
        services.GetRequiredService<IAuthorizedRealtimeDispatcher<THub>>().SendShopAsync(shop, "HistoryFixture", message, Ct);
}
