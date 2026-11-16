using System.Net;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0303Tests
{
    [Fact]
    public async Task SimulatorDelayedCaptureAndDefinitiveFailureRemainDistinct()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await P0302Fixture.StartAsync(database, clock);
        var (payer, _, shops, quote) = await fixture.AcceptedQuoteAsync(1);
        var replacement = await OtherAcceptedQuoteAsync(payer, quote, shops[0].ShopId);

        var uncertain = (await StartAsync(payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        var path = CapturePath(quote, uncertain);
        await PostAsync(payer.Client, path, new { outcome = "Timeout" });
        AssertStatus(await VerifyAsync(payer.Client, quote, uncertain), "PendingVerification", "NotFulfilled", "NotApplicable");
        await PostAsync(payer.Client, path, new { outcome = "Failed" });
        AssertStatus(await VerifyAsync(payer.Client, quote, uncertain), "Failed", "NotFulfilled", "NotApplicable");
        await PostAsync(payer.Client, path, new { outcome = "Captured" });
        AssertStatus(await VerifyAsync(payer.Client, quote, uncertain), "Failed", "NotFulfilled", "NotApplicable");
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));

        var delayed = (await StartAsync(payer.Client, replacement, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        var delayedPath = CapturePath(replacement, delayed);
        await PostAsync(payer.Client, delayedPath, new { outcome = "Delayed" });
        clock.Now = clock.Now.AddSeconds(30).AddTicks(-10);
        AssertStatus(await VerifyAsync(payer.Client, replacement, delayed), "PendingVerification", "NotFulfilled", "NotApplicable");
        clock.Now = clock.Now.AddTicks(10);
        AssertStatus(await VerifyAsync(payer.Client, replacement, delayed), "Verified", "Fulfilled", "Applying");
        await PostAsync(payer.Client, delayedPath, new { outcome = "Failed" });
        AssertStatus(await VerifyAsync(payer.Client, replacement, delayed), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, replacement, 1);
    }
}
