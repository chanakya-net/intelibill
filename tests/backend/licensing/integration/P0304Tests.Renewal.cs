using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Tests;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Razorpay's created_at is the payment's creation time, not its capture time. A renewal is granted only when the
/// verification instant and the creation time classify identically; otherwise the funds are kept for review.
/// </summary>
public sealed partial class P0304Tests
{
    private sealed record RenewalRun(P0308Fixture Fixture, Package Package, FakeRazorpay Fake, Guid FirstTerm, CheckoutQuoteV1 Quote, Guid Attempt);

    private async Task<RenewalRun> StartRenewalAsync(DateTimeOffset createdAt)
    {
        var fake = new FakeRazorpay();
        var fixture = await StartAsync(database, March31, FakeRazorpay.Settings(),
            services: services => services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => fake));
        fake.Clock = () => fixture.Checkout.CheckoutClock.Now;
        var package = await fixture.PackageAsync(1);
        var first = await AcceptAsync(package, await FirstQuoteAsync(package, "Month"));
        var firstAttempt = await StartPaymentAsync(package, first);
        fake.AddPayment(fake.OrderId(), "captured");
        var paid = await P0302Fixture.VerifyAsync(package.Payer.Client, first, firstAttempt);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");

        fixture.At(createdAt);
        var quote = await AcceptAsync(package, await RenewalQuoteAsync(package, paid.GetProperty("termId").GetGuid(), "Month"));
        var attempt = await StartPaymentAsync(package, quote);
        return new(fixture, package, fake, paid.GetProperty("termId").GetGuid(), quote, attempt);
    }

    private static string OrderOf(FakeRazorpay fake, int index) => fake.OrderIds()[index];

    [Fact]
    public async Task CreationInsideGraceButObservationAfterGraceKeepsFundsForReviewWithoutRenewing()
    {
        var created = April30.AddDays(2).AddHours(3);
        var run = await StartRenewalAsync(created);
        await using var _ = run.Fixture;
        run.Fake.AddPayment(OrderOf(run.Fake, 1), "captured", createdAt: created.ToUnixTimeSeconds());

        run.Fixture.At(April30.AddDays(7).AddHours(1));
        var status = await P0302Fixture.VerifyAsync(run.Package.Payer.Client, run.Quote, run.Attempt);

        Assert.Equal("Verified", status.GetProperty("payment").GetString());
        Assert.Equal("NeedsReview", status.GetProperty("fulfilment").GetString());
        Assert.Equal(1L, await run.Fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(2L, await run.Fixture.CountAsync("verified_payments"));
        Assert.Equal(2L, await run.Fixture.CountAsync("verified_payments", "capture_time_basis = 'ObservedAtVerification' AND captured_at = verified_at"));
    }

    [Fact]
    public async Task CreationAndObservationInTheSameGraceWindowRenewsWithWholeSecondTimestamps()
    {
        // The attempt exists at .500 s; the provider reports its whole-second creation time just before it.
        var attemptAt = April30.AddDays(2).AddHours(3).AddMilliseconds(500);
        var run = await StartRenewalAsync(attemptAt);
        await using var _ = run.Fixture;
        run.Fake.AddPayment(OrderOf(run.Fake, 1), "captured", createdAt: attemptAt.ToUnixTimeSeconds());

        run.Fixture.At(attemptAt.AddHours(1));
        var status = await P0302Fixture.VerifyAsync(run.Package.Payer.Client, run.Quote, run.Attempt);

        P0302Fixture.AssertStatus(status, "Verified", "Fulfilled", "Applying");
        Assert.Equal(April30, status.GetProperty("termStartsAt").GetDateTimeOffset());
        Assert.Equal(2L, await run.Fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(1L, await run.Fixture.CountAsync("payment_fulfilments", "grace_days_included = 3"));
        Assert.Equal(2L, await run.Fixture.CountAsync("verified_payments", "capture_time_basis = 'ObservedAtVerification'"));
    }
}
