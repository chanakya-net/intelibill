using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0703Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LicensingMutationAndSmtpHandoffAreOrdered(bool acknowledge)
    {
        await using var peer = new P0702SmtpPeer(pauseBeforeData: true);
        var clock = new P0201Clock { Now = Start };
        await using var paid = await StartPeerFixtureAsync(peer, clock);
        var package = await QueuePaidReminderAsync(paid);
        var delivery = DeliverAsync(paid);
        await peer.BeforeData.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Sending", await StateAsync(paid, package));
        var close = P0417Tests.CloseShopAsync(package.Payer, package.Account);
        try
        {
            Assert.True(await HasWaitingAccountWriterAsync(paid, package, close), "Closure completed without waiting for SMTP.");
            Assert.False(await DeliverAsync(paid));
        }
        finally
        {
            peer.AllowData();
            peer.Finish(acknowledge);
        }
        await close;
        Assert.True(await delivery);
        Assert.Equal(acknowledge ? "SmtpAccepted" : "Uncertain", await StateAsync(paid, package));
        Assert.False(await DeliverAsync(paid));
        Assert.Equal(1, peer.Submissions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LicensingMutationAndSmtpHandoffAreOrderedAfterLease(bool reclaim)
    {
        await using var peer = new P0702SmtpPeer();
        var clock = new P0201Clock { Now = Start };
        await using var paid = await StartPeerFixtureAsync(peer, clock);
        var package = await QueuePaidReminderAsync(paid);
        var gate = peer.PauseNextGreeting();
        var worker = DeliverAsync(paid);
        await gate.Connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal("Leased", await StateAsync(paid, package));
            await P0417Tests.CloseShopAsync(package.Payer, package.Account);
            if (reclaim)
            {
                clock.Now = clock.Now.AddSeconds(60);
                Assert.True(await DeliverAsync(paid));
                Assert.Equal("Suppressed", await StateAsync(paid, package));
            }
        }
        finally
        {
            gate.Release.TrySetResult();
            peer.Finish(true);
        }
        Assert.True(await worker);
        Assert.False(await DeliverAsync(paid));
        Assert.Equal("Suppressed", await StateAsync(paid, package));
        Assert.Equal(0, peer.Submissions);
        Assert.Equal(reclaim ? 2L : 1L, await paid.CountAsync("notification_email_events", "state = 'Leased'"));
    }

    [Fact]
    public async Task LicensingMutationAndSmtpHandoffAreOrderedOnCancellationWithSingleConnection()
    {
        await using var peer = new P0702SmtpPeer();
        var clock = new P0201Clock { Now = Start };
        await using var paid = await StartPeerFixtureAsync(peer, clock, poolSize: 1);
        var package = await QueuePaidReminderAsync(paid);
        using var stop = new CancellationTokenSource();
        var sending = DeliverAsync(paid, stop.Token);
        await peer.Received.WaitAsync(TimeSpan.FromSeconds(10));
        await stop.CancelAsync();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        }
        finally
        {
            peer.Finish(false);
        }
        Assert.Equal("Sending", await StateAsync(paid, package));
        await P0417Tests.CloseShopAsync(package.Payer, package.Account);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.True(await DeliverAsync(paid));
        Assert.Equal("Uncertain", await StateAsync(paid, package));
        Assert.False(await DeliverAsync(paid));
        Assert.Equal(1, peer.Submissions);
        Assert.Equal(false, await paid.ScalarAsync($"""
            SELECT EXISTS (SELECT FROM pg_locks WHERE locktype = 'advisory'
                AND classid::bigint = ((hashtextextended('b{package.Account:D}', 0) >> 32) & 4294967295)
                AND objid::bigint = (hashtextextended('b{package.Account:D}', 0) & 4294967295))
            """));
    }

    private static async Task<bool> HasWaitingAccountWriterAsync(P0308Fixture paid, Package package, Task writer)
    {
        var elapsed = Stopwatch.StartNew();
        while (!writer.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(3))
        {
            var waiting = await paid.ScalarAsync($"""
                SELECT EXISTS (SELECT FROM pg_locks WHERE locktype = 'advisory' AND NOT granted
                    AND classid::bigint = ((hashtextextended('b{package.Account:D}', 0) >> 32) & 4294967295)
                    AND objid = (hashtextextended('b{package.Account:D}', 0) & 4294967295)::oid)
                """);
            if (waiting is true)
                return true;
            await Task.Delay(10);
        }
        return false;
    }

    private async Task<P0308Fixture> StartPeerFixtureAsync(P0702SmtpPeer peer, P0201Clock leaseClock, int poolSize = 4)
    {
        var settings = Settings(mailpit);
        settings["AdminMail:Host"] = "127.0.0.1";
        settings["AdminMail:Port"] = peer.Port.ToString(CultureInfo.InvariantCulture);
        settings["LicensingDatabase:MaxPoolSize"] = poolSize.ToString(CultureInfo.InvariantCulture);
        return await P0308Fixture.StartAsync(database, Start, settings,
            services: services => services.AddKeyedSingleton<TimeProvider>("billing-email-lease-clock", leaseClock));
    }

    private static async Task<Package> QueuePaidReminderAsync(P0308Fixture paid)
    {
        var package = await paid.PackageAsync(1);
        await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        paid.At(Due);
        return package;
    }
}
