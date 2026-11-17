using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog.Core;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0305Tests
{
    [Fact]
    public async Task RetiredAttemptStaysPinnedAndOwnerCanRequeueAfterItsAdapterReturns()
    {
        await using var rig = await StartAsync();
        await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var payment = rig.Fake.AddPayment(order, "captured");
        var clock = rig.Fixture.CheckoutClock;
        await using var switched = rig.Fixture.Hosts.Licensing.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Payments:Razorpay:ConfigurationVersion"] = "fixture/2" })));
        using var client = switched.CreateClient();
        var body = Body(rig, order, payment);
        await DeliverAsync(client, body, "rotated_capture");
        var calls = rig.Fake.Calls.Count;

        for (var index = 0; index < 12; index++)
        {
            await DrainAsync(switched.Services, clock);
            clock.Now = clock.Now.AddMinutes(5);
        }
        await AssertWorkAsync(rig, "DeadLetter", "AdapterUnavailable", failure: true);
        Assert.Equal(calls, rig.Fake.Calls.Count);
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        using var oldEndpoint = rig.Fixture.Hosts.Licensing.CreateClient();
        await DeliverAsync(oldEndpoint, body, "rotated_capture");
        Assert.Equal(1L, await rig.Fixture.CountAsync("gateway_inbox_events", "configuration_version = 'fixture/2'"));
        Assert.Equal(1L, await rig.Fixture.CountAsync("gateway_inbox_events"));
        await AssertWorkAsync(rig, "DeadLetter", "AdapterUnavailable", failure: true);

        Assert.True(Assert.IsType<bool>(await LicensingPostgreSqlFixture.ReadScalarAsync(rig.Fixture.Hosts.Fixture.Owner, """
            SELECT public.requeue_gateway_inbox_event(inbox_id, received_at) FROM gateway_inbox_events
            """)));
        await DrainAsync(rig.Fixture.Hosts.Licensing.Services, clock);
        await AssertWorkAsync(rig, "Processed", "Fulfilled");
        Assert.Equal(1L, await rig.Fixture.CountAsync("verified_payments", "configuration_version = 'fixture/1'"));
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
        await RecordAsync("webhook-pinned-requeue.json", new
        {
            deliveryPin = "fixture/2", verifiedPin = "fixture/1", detectedUnavailable = true, ownerRequeued = true,
            duplicateAfterRotation = true, fulfilments = 1
        });
    }

    [Fact]
    public async Task BackgroundWorkerDrainsAndLogsDeadLetterUsingOnlyInternalIdentity()
    {
        var logs = new P0107Tests.CapturedLogs();
        await using var rig = await StartAsync(new Dictionary<string, string?> { ["Payments:ReconcileInBackground"] = "true" },
            services => services.AddSingleton<ILogEventSink>(logs));
        await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var payment = rig.Fake.AddPayment(order, "captured");
        using var client = rig.Fixture.Hosts.Licensing.CreateClient();
        await DeliverAsync(client, Body(rig, order, payment), "auto_capture");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (await rig.Fixture.CountAsync("gateway_inbox_work", "outcome = 'Fulfilled'") == 0)
            await Task.Delay(100, timeout.Token);

        await DeliverAsync(client, Body(rig, order, "pay_other"), "auto_duplicate_funds");
        await logs.WaitForAsync("dead-lettered (DifferentPayment)");
        await AssertWorkAsync(rig, "DeadLetter", "DifferentPayment", failure: true);
        Assert.DoesNotContain(logs.Events, line => line.Contains("auto_duplicate_funds", StringComparison.Ordinal) ||
            line.Contains(PayerMarker, StringComparison.Ordinal) || line.Contains(Secret, StringComparison.Ordinal));
        AssertStatus(await StatusAsync(rig.Payer.Client, rig.Quote), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
    }
}
