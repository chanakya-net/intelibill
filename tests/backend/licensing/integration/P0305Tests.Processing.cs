using System.Net;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0305Tests
{
    [Fact]
    public async Task RestartAfterReceiptResumesAndReorderedEventsKeepOneSettlement()
    {
        await using var rig = await StartAsync();
        await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var payment = rig.Fake.AddPayment(order, "captured");
        using var client = rig.Fixture.Hosts.Licensing.CreateClient();

        await DeliverAsync(client, Body(rig, order, payment, "payment.failed"), "early_failure");
        await DeliverAsync(client, Body(rig, order, payment), "capture");
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));

        await using var restarted = rig.Fixture.Hosts.Licensing.WithWebHostBuilder(_ => { });
        await DrainAsync(restarted.Services, rig.Fixture.CheckoutClock);
        await AssertWorkAsync(rig, "Processed", "Fulfilled");
        AssertStatus(await StatusAsync(rig.Payer.Client, rig.Quote), "Verified", "Fulfilled", "Applying");
        Assert.Contains(rig.Fake.Calls, call => call == $"GET /v1/orders/{order}");
        Assert.Contains(rig.Fake.Calls, call => call == $"GET /v1/orders/{order}/payments");
        Assert.Contains(rig.Fake.Calls, call => call == $"GET /v1/payments/{payment}");
        var calls = rig.Fake.Calls.Count;

        await DeliverAsync(client, Body(rig, order, payment), "capture");
        await DeliverAsync(client, Body(rig, order, payment, "order.paid"), "order_paid");
        await DeliverAsync(client, Body(rig, order, payment, "payment.failed"), "late_failure");
        await DeliverAsync(client, Body(rig, order, payment, "payment.authorized"), "authorization");
        await DeliverAsync(client, Body(rig, order, "pay_second"), "different_payment");
        await DrainAsync(restarted.Services, rig.Fixture.CheckoutClock);
        await AssertWorkAsync(rig, "Processed", "AlreadyVerified");
        await AssertWorkAsync(rig, "DeadLetter", "DifferentPayment", failure: true);
        Assert.Equal(3L, await rig.Fixture.CountAsync("gateway_inbox_work", "outcome = 'NoAction'"));
        Assert.Equal(calls + 1, rig.Fake.Calls.Count);
        Assert.Equal($"GET /v1/orders/{order}/payments", rig.Fake.Calls.Last());
        Assert.Equal(0L, await rig.Fixture.CountAsync("unmatched_payments"));
        AssertStatus(await StatusAsync(rig.Payer.Client, rig.Quote), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
        await RecordAsync("webhook-ingress-and-recovery.json", new
        {
            durableBeforeProcessing = true, restartedHost = true, duplicates = 1, verified = 1, fulfilments = 1,
            providerReads = rig.Fake.Calls.Where(call => call.StartsWith("GET ", StringComparison.Ordinal)),
            failureAndAuthorizationIgnored = 3, secondPaymentDeadLetter = true
        });
    }

    [Fact]
    public async Task AnExpiredLeaseCannotApplyFundsAfterTakeover()
    {
        await using var rig = await StartAsync();
        await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var payment = rig.Fake.AddPayment(order, "captured");
        var clock = rig.Fixture.CheckoutClock;
        var services = rig.Fixture.Hosts.Licensing.Services;
        using var client = rig.Fixture.Hosts.Licensing.CreateClient();
        await DeliverAsync(client, Body(rig, order, payment), "capture");
        var first = Assert.IsType<GatewayInboxLease>(await ClaimAsync(services, clock));
        rig.Fake.HoldWhen = call => call == $"GET /v1/orders/{order}";
        var stale = ProcessAsync(services, first);
        await using var restarted = rig.Fixture.Hosts.Licensing.WithWebHostBuilder(_ => { });

        try
        {
            await rig.Fake.HoldReached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Now = clock.Now.AddSeconds(30).AddTicks(-10);
            Assert.Null(await ClaimAsync(restarted.Services, clock));
            clock.Now = clock.Now.AddTicks(10);
            var recovered = Assert.IsType<GatewayInboxLease>(await ClaimAsync(restarted.Services, clock));
            Assert.Equal(first.InboxId, recovered.InboxId);
            Assert.NotEqual(first.Token, recovered.Token);
            // The expired worker still has a valid provider response, but cannot apply it under its old lease.
            rig.Fake.ReleaseHeld.TrySetResult();
            Assert.Null(await stale);
            Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
            await Task.WhenAll(ProcessAsync(restarted.Services, recovered), rig.VerifyAsync(first.AttemptId!.Value, null, null));
        }
        finally
        {
            rig.Fake.ReleaseHeld.TrySetResult();
            await stale;
        }
        Assert.Equal(1L, await rig.Fixture.CountAsync("gateway_inbox_work", "status = 'Processed' AND attempts = 2"));
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
        await RecordAsync("webhook-lease.json", new { expiredAtSeconds = 30, claims = 2, staleApplied = false, fulfilments = 1 });
    }

    [Fact]
    public async Task UnprovenCaptureRetriesAndUnknownOrderDeadLettersAtTheBudget()
    {
        await using var rig = await StartAsync();
        await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var payment = rig.Fake.AddPayment(order, "authorized");
        var clock = rig.Fixture.CheckoutClock;
        var services = rig.Fixture.Hosts.Licensing.Services;
        using var client = rig.Fixture.Hosts.Licensing.CreateClient();
        await DeliverAsync(client, Body(rig, order, payment), "unproven");
        await DeliverAsync(client, Body(rig, "order_unknown", "pay_unknown"), "unknown");

        await DrainAsync(services, clock);
        await AssertWorkAsync(rig, "Pending", "CaptureUnproven", failure: true);
        await AssertWorkAsync(rig, "Pending", "UnknownOrder", failure: true);
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        clock.Now = clock.Now.AddSeconds(4).AddTicks(-10);
        Assert.Null(await ClaimAsync(services, clock));
        clock.Now = clock.Now.AddTicks(10);
        rig.Fake.Transition(payment, "captured");

        for (var index = 1; index < 12; index++)
        {
            await DrainAsync(services, clock);
            clock.Now = clock.Now.AddMinutes(5);
        }
        await AssertWorkAsync(rig, "Processed", "Fulfilled");
        await AssertWorkAsync(rig, "DeadLetter", "UnknownOrder", failure: true);
        Assert.Equal(1L, await rig.Fixture.CountAsync("gateway_inbox_work", "status = 'DeadLetter' AND attempts = 12"));
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
        await RecordAsync("webhook-retry.json", new { firstBackoffSeconds = 4, unknownAttempts = 12, captureRecovered = true });
    }

    private static Task AssertWorkAsync(P0304Tests.Rig rig, string status, string code, bool failure = false) =>
        AssertWorkAsync(rig.Fixture, status, code, failure);

    private static async Task AssertWorkAsync(P0302Fixture fixture, string status, string code, bool failure = false)
    {
        var column = failure ? "last_failure" : "outcome";
        Assert.Equal(1L, await fixture.CountAsync("gateway_inbox_work", $"status = '{status}' AND {column} = '{code}'"));
    }

    internal static async Task DrainAsync(IServiceProvider services, TimeProvider clock)
    {
        while (await ClaimAsync(services, clock) is { } lease)
            await ProcessAsync(services, lease);
    }

    private static async Task<GatewayInboxLease?> ClaimAsync(IServiceProvider services, TimeProvider clock)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IGatewayInboxStore>()
            .ClaimAsync(clock.GetUtcNow(), CancellationToken.None);
    }

    private static async Task<GatewayInboxResolution?> ProcessAsync(IServiceProvider services, GatewayInboxLease lease)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProcessGatewayInboxHandler>()
            .HandleAsync(lease, CancellationToken.None);
    }

    private static async Task RecordAsync(string name, object observations)
    {
        if (Environment.GetEnvironmentVariable("P0305_EVIDENCE_DIR") is { Length: > 0 } evidence)
            await File.WriteAllTextAsync(System.IO.Path.Combine(evidence, name), JsonSerializer.Serialize(observations));
    }

    private static async Task DeliverAsync(HttpClient client, string body, string eventId)
    {
        using var response = await SendAsync(client, body, eventId);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
