using System.Net;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// First ambiguous reads, stale resume observations and interrupted response bodies: each keeps the original order
/// pending verification, with no launcher and no grant, until a genuine capture or a fresh proof of an unpaid order.
/// </summary>
public sealed partial class P0304Tests
{
    [Theory]
    [InlineData(false, "in-flight-payment")]
    [InlineData(false, "incomplete-totals")]
    [InlineData(true, "in-flight-payment")]
    [InlineData(true, "incomplete-totals")]
    public async Task FirstProofFreeReadOfAmbiguousFundingStopsOfferingTheLauncher(bool lostCreate, string evidence)
    {
        await using var rig = await Rig.StartAsync(database);
        rig.Fake.LoseCreateResponse = lostCreate;
        var started = await StartAsync(rig.Payer.Client, rig.Quote, HttpStatusCode.Created);
        rig.Fake.LoseCreateResponse = false;
        var attempt = started.GetProperty("attemptId").GetGuid();
        var order = rig.Fake.OrderId();
        if (evidence == "in-flight-payment")
            rig.Fake.AddPayment(order, "created");
        else
            rig.Fake.Order(order).Remove("amount_due");

        await AssertHeldWithoutLauncherAsync(rig, await rig.VerifyAsync(attempt, null, null));
        Assert.Equal(1L, await rig.Fixture.CountAsync("payment_attempt_sessions"));

        rig.Fake.Order(order)["amount_due"] = rig.Quote.AmountPaise;
        rig.Fake.AddPayment(order, "captured");
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        Assert.Equal(1, rig.Fake.Posts);
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
    }

    [Fact]
    public async Task IdleUnpaidOrderStaysPayableOnAProofFreeCheck()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var status = await rig.VerifyAsync(attempt, null, null);
        AssertStatus(status, "AwaitingPayment", "NotFulfilled", "NotApplicable");
        Assert.Equal(rig.Fake.OrderId(), status.GetProperty("launch").GetProperty("parameters").GetProperty("orderId").GetString());
    }

    [Theory]
    [InlineData("claim")]
    [InlineData("authorized")]
    public async Task StaleUnpaidReadNeverClearsNewerUncertainty(string newer)
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        AssertStatus(await rig.VerifyAsync(attempt, "Success", null), "PendingVerification", "NotFulfilled", "NotApplicable");

        // The proof-free recheck reads the order unpaid, then its last answer is held in flight.
        rig.Fake.HoldWhen = call => call == $"GET /v1/orders/{order}/payments";
        var stale = rig.VerifyAsync(attempt, null, null);
        await rig.Fake.HoldReached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        string? authorized = null;
        if (newer == "claim")
            AssertStatus(await rig.VerifyAsync(attempt, "Success", null), "PendingVerification", "NotFulfilled", "NotApplicable");
        else
        {
            authorized = rig.Fake.AddPayment(order, "authorized");
            AssertStatus(await rig.VerifyAsync(attempt, null, null), "PendingVerification", "NotFulfilled", "NotApplicable");
        }
        rig.Fake.ReleaseHeld.SetResult();

        await AssertHeldWithoutLauncherAsync(rig, await stale);

        // A later genuinely unpaid recheck still resumes the same remote order.
        if (authorized is not null)
            (rig.Fake.Payment(authorized)["status"], rig.Fake.Payment(authorized)["captured"]) = ("failed", false);
        var resumed = await rig.VerifyAsync(attempt, null, null);
        AssertStatus(resumed, "AwaitingPayment", "NotFulfilled", "NotApplicable");
        Assert.Equal(order, resumed.GetProperty("launch").GetProperty("parameters").GetProperty("orderId").GetString());
        Assert.Equal(1, rig.Fake.Posts);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("order-read")]
    [InlineData("payment-read")]
    public async Task ResponseEndingMidBodyStaysPendingAndRecoversTheOriginalOrder(string truncated)
    {
        await using var rig = await Rig.StartAsync(database);
        if (truncated == "create")
            rig.Fake.TruncateWhen = call => call == "POST /v1/orders";
        var started = await StartAsync(rig.Payer.Client, rig.Quote, HttpStatusCode.Created);
        var attempt = started.GetProperty("attemptId").GetGuid();
        var order = rig.Fake.OrderId();
        if (truncated == "create")
            AssertStatus(started, "PendingVerification", "NotFulfilled", "NotApplicable");
        else
        {
            if (truncated == "payment-read")
                rig.Fake.AddPayment(order, "captured");
            rig.Fake.TruncateWhen = truncated == "order-read"
                ? call => call == $"GET /v1/orders/{order}"
                : call => call.StartsWith("GET /v1/payments/", StringComparison.Ordinal);
            await AssertHeldWithoutLauncherAsync(rig, await rig.VerifyAsync(attempt, null, null));
        }

        if (truncated != "payment-read")
        {
            var recovered = await rig.VerifyAsync(attempt, null, null);
            AssertStatus(recovered, "AwaitingPayment", "NotFulfilled", "NotApplicable");
            Assert.Equal(order, recovered.GetProperty("launch").GetProperty("parameters").GetProperty("orderId").GetString());
            rig.Fake.AddPayment(order, "captured");
        }
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        Assert.Equal(1, rig.Fake.Posts);
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
    }

    private static async Task AssertHeldWithoutLauncherAsync(Rig rig, JsonElement verified)
    {
        foreach (var status in new[] { verified, await StatusAsync(rig.Payer.Client, rig.Quote) })
        {
            AssertStatus(status, "PendingVerification", "NotFulfilled", "NotApplicable");
            Assert.Equal(JsonValueKind.Null, status.GetProperty("launch").ValueKind);
        }
        Assert.Equal(1, rig.Fake.Posts);
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));
    }
}
