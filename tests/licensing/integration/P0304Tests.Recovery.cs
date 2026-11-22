using System.Net;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;
using static Intelibill.Licensing.Integration.Tests.P0304Tests;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>Original-order recovery: a transient read failure never strands an unpaid order, and nothing else reopens it.</summary>
public sealed partial class P0304TestsRecovery(LicensingPostgreSqlFixture database)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task LostCreateAndFailedFirstVerificationReadRecoverTheSameUnpaidOrderWithOnePost()
    {
        await using var rig = await Rig.StartAsync(database);
        rig.Fake.LoseCreateResponse = true;
        var attempt = (await StartAsync(rig.Payer.Client, rig.Quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        rig.Fake.LoseCreateResponse = false;
        rig.Fake.FailOrderReads = 1;

        // The order lookup succeeds and records the original order; the read that follows fails transiently.
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(1L, await rig.Fixture.CountAsync("payment_attempt_sessions"));
        Assert.Equal(0, rig.Fake.FailOrderReads);

        var recovered = await rig.VerifyAsync(attempt, null, null);
        AssertStatus(recovered, "AwaitingPayment", "NotFulfilled", "NotApplicable");
        Assert.Equal(rig.Fake.OrderId(), recovered.GetProperty("launch").GetProperty("parameters").GetProperty("orderId").GetString());
        AssertStatus(await StatusAsync(rig.Payer.Client, rig.Quote), "AwaitingPayment", "NotFulfilled", "NotApplicable");

        rig.Fake.AddPayment(rig.Fake.OrderId(), "captured");
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "Verified", "Fulfilled", "Applying");
        Assert.Equal(1, rig.Fake.Posts);
        await P0302Tests.AssertSingleFulfilmentAsync(rig.Fixture, rig.Quote, 1);
    }

    [Theory]
    [InlineData("paid-order-empty-collection")]
    [InlineData("amount-paid-without-payment")]
    [InlineData("authorized-payment")]
    [InlineData("in-flight-payment")]
    [InlineData("amount-due-absent")]
    public async Task UncertainAttemptOffersNoLauncherUnlessTheOrderIsProvablyUnpaid(string evidence)
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        rig.Fake.FailOrderReads = 1;
        AssertStatus(await rig.VerifyAsync(attempt, null, null), "PendingVerification", "NotFulfilled", "NotApplicable");
        var entity = rig.Fake.Order(order);
        switch (evidence)
        {
            case "paid-order-empty-collection":
                (entity["status"], entity["amount_paid"], entity["amount_due"]) = ("paid", rig.Quote.AmountPaise, 0);
                break;
            case "amount-paid-without-payment": entity["amount_paid"] = rig.Quote.AmountPaise; break;
            case "authorized-payment": rig.Fake.AddPayment(order, "authorized"); break;
            case "in-flight-payment": rig.Fake.AddPayment(order, "created"); break;
            default: entity.Remove("amount_due"); break;
        }

        foreach (var status in new[] { await rig.VerifyAsync(attempt, null, null), await StatusAsync(rig.Payer.Client, rig.Quote) })
        {
            AssertStatus(status, "PendingVerification", "NotFulfilled", "NotApplicable");
            Assert.Equal(JsonValueKind.Null, status.GetProperty("launch").ValueKind);
        }
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));
    }

    [Fact]
    public async Task PaidOrderWithAnEmptyCollectionNeverExposesTheLauncherOnAFreshAttempt()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var entity = rig.Fake.Order(rig.Fake.OrderId());
        (entity["status"], entity["amount_paid"], entity["amount_due"]) = ("paid", rig.Quote.AmountPaise, 0);

        var status = await rig.VerifyAsync(attempt, null, null);
        AssertStatus(status, "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(JsonValueKind.Null, status.GetProperty("launch").ValueKind);
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
    }

    [Fact]
    public async Task BrowserClaimIsNeverClearedByTheSameRequestButAProofFreeRecheckOfAnUnpaidOrderResumesIt()
    {
        await using var rig = await Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var forged = new Dictionary<string, string>
        {
            ["razorpay_payment_id"] = "pay_forged000001", ["razorpay_order_id"] = order, ["razorpay_signature"] = new('c', 64),
        };

        AssertStatus(await rig.VerifyAsync(attempt, "Success", forged), "PendingVerification", "NotFulfilled", "NotApplicable");
        AssertStatus(await rig.VerifyAsync(attempt, "Success", null), "PendingVerification", "NotFulfilled", "NotApplicable");
        var resumed = await rig.VerifyAsync(attempt, null, null);
        AssertStatus(resumed, "AwaitingPayment", "NotFulfilled", "NotApplicable");
        Assert.Equal(order, resumed.GetProperty("launch").GetProperty("parameters").GetProperty("orderId").GetString());
        Assert.Equal(1, rig.Fake.Posts);
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
    }
}
