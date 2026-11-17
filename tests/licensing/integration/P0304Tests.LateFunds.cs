using System.Text;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Late provider transitions: a timed-out failure that may still authorize, an unsigned success report and a paid
/// order carrying a separately auto-refunded late authorization.
/// </summary>
public sealed partial class P0304Tests
{
    /// <summary>R004: Razorpay may move a timed-out Failed payment to Authorized during its late-authorization window.</summary>
    [Fact]
    public async Task TimedOutFailedPaymentKeepsTheOriginalOrderPendingUntilItResolves()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        rig.Fake.FailOrderReads = 1;
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "PendingVerification", "NotFulfilled", "NotApplicable");
        var payment = rig.Fake.AddPayment(order, "failed");
        rig.Fake.Payment(payment)["error_reason"] = "payment_timeout";
        rig.Fake.Order(order)["status"] = "attempted";

        rig.Fixture.CheckoutClock.Now = rig.Fixture.CheckoutClock.Now.AddMinutes(15);
        await AssertHeldWithoutLauncherAsync(rig, await rig.VerifyAsync(attempt, null, null));
        rig.Fake.Transition(payment, "authorized");
        await AssertHeldWithoutLauncherAsync(rig, await rig.VerifyAsync(attempt, null, null));
        rig.Fake.Transition(payment, "captured");

        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        Assert.Equal(1, rig.Fake.Posts);
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
    }

    /// <summary>A failure that outlived the late-authorization window rules out the debit: the same order resumes.</summary>
    [Fact]
    public async Task FailedPaymentPastTheLateAuthorizationWindowResumesTheSameOrder()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        rig.Fake.FailOrderReads = 1;
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "PendingVerification", "NotFulfilled", "NotApplicable");
        rig.Fake.AddPayment(order, "failed");
        rig.Fake.Order(order)["status"] = "attempted";

        rig.Fixture.CheckoutClock.Now = rig.Fixture.CheckoutClock.Now.AddDays(4).AddSeconds(1);
        var resumed = await rig.VerifyAsync(attempt, null, null);
        AssertStatus(resumed, "AwaitingPayment", "NotFulfilled", "NotApplicable");
        Assert.Equal(order, resumed.GetProperty("launch").GetProperty("parameters").GetProperty("orderId").GetString());
        Assert.Equal(1, rig.Fake.Posts);
    }

    /// <summary>R014: a reported Razorpay success must carry a valid signature, whether it is forged, omitted or null.</summary>
    [Theory]
    [InlineData("forged")]
    [InlineData("omitted")]
    [InlineData("null")]
    public async Task UnsignedOrForgedSuccessAgainstACapturedOrderGrantsNothingUntilAProofFreeRecheck(string proof)
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var payment = rig.Fake.AddPayment(order, "captured");
        var body = proof switch
        {
            "forged" => JsonSerializer.Serialize(new
            {
                launcherOutcome = "Success",
                launcherProof = new Dictionary<string, string>
                {
                    ["razorpay_payment_id"] = payment, ["razorpay_order_id"] = order, ["razorpay_signature"] = new('b', 64),
                },
            }),
            "omitted" => """{"launcherOutcome":"Success"}""",
            _ => """{"launcherOutcome":"Success","launcherProof":null}""",
        };

        for (var report = 0; report < 2; report++)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await rig.Payer.Client.PostAsync(VerifyPath(rig.Quote, attempt), content);
            var status = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.True(response.IsSuccessStatusCode, status.GetRawText());
            AssertStatus(status, "PendingVerification", "NotFulfilled", "NotApplicable");
            Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
            Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));
        }

        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        Assert.Equal(1, rig.Fake.Posts);
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
    }

    /// <summary>
    /// R015: Razorpay auto-refunds a duplicate late authorization on an already paid order without capturing it. That
    /// entry is not a second capture, provided its own read agrees that it was never captured and fully refunded.
    /// </summary>
    [Theory]
    [InlineData("auto-refunded", true)]
    [InlineData("partial-refund", false)]
    [InlineData("refund-pending", false)]
    [InlineData("entity-disagrees", false)]
    [InlineData("captured-then-refunded", false)]
    public async Task PaidOrderWithASeparatelyAutoRefundedAuthorizationSettlesOnlyOnConsistentEvidence(string variant,
        bool settles)
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        rig.Fake.AddPayment(order, "captured");
        var late = rig.Fake.AddPayment(order, "refunded");
        var entity = rig.Fake.Payment(late);
        var full = rig.Quote.AmountPaise;
        (entity["amount_refunded"], entity["refund_status"]) = (full, "full");
        switch (variant)
        {
            case "partial-refund": (entity["amount_refunded"], entity["refund_status"]) = (full - 1, "partial"); break;
            case "refund-pending": entity["refund_status"] = null; break;
            case "entity-disagrees": rig.Fake.PatchCollectionEntry(late, "amount_refunded", full - 1); break;
            case "captured-then-refunded": (entity["captured"], entity["amount_captured"]) = (true, full); break;
        }

        var status = await rig.VerifyAsync(attempt, null, null);
        if (settles)
        {
            AssertStatus(status, "Verified", "Fulfilled", "Applying");
            Assert.Equal(1L, await rig.Fixture.CountAsync("verified_payments"));
            await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
            return;
        }
        AssertStatus(status, "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));
    }
}
