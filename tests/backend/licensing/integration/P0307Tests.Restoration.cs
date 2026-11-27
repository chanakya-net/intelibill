using System.Net;
using Intelibill.Licensing.Application.Payments;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0307Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeferredRefundDecisionKeepsOriginalRestorationReceiptOnHold(bool manual)
    {
        await using var fixture = await P0308Fixture.StartAsync(database, March31, P0306Tests.Settings);
        var package = await fixture.PackageAsync(1);
        var quote = await P0308Fixture.AcceptAsync(package, await P0308Fixture.FirstQuoteAsync(package, "Month"));
        await SetRestrictionAsync(fixture, package.Account, released: false);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var command = P0306Tests.Receipt(quote, March31);
        Guid? attempt = null;
        if (manual)
        {
            using var receipt = await P0212Fixture.PostAsync(finance, P0306Tests.Path(quote), command);
            Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        }
        else
        {
            attempt = await P0308Fixture.StartPaymentAsync(package, quote);
            await CaptureAsync(package.Payer.Client, quote, attempt.Value);
            await VerifyAsync(package.Payer.Client, quote, attempt.Value);
        }
        var review = await SingleCaseAsync(finance, package.Account);
        Assert.Equal("RestorationPending", review.GetProperty("classification").GetString());
        var path = $"{ReviewPath(package.Account)}/{review.GetProperty("caseId").GetGuid():D}/decisions";
        using var referred = await P0212Fixture.PostAsync(finance, path, new ReviewPaymentCommand(Guid.NewGuid(), 0,
            "ReferForRefundReview", "Refer the original funds for review", "FIXTURE-REFERENCE"));
        Assert.Equal(HttpStatusCode.Created, referred.StatusCode);

        fixture.At(March31.AddDays(3));
        await SetRestrictionAsync(fixture, package.Account, released: true);
        if (attempt is { } id)
            await VerifyAsync(package.Payer.Client, quote, id);
        else
        {
            using var replay = await P0212Fixture.PostAsync(finance, P0306Tests.Path(quote), command);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        }
        AssertStatus(await StatusAsync(package.Payer.Client, quote), "Verified", "NeedsReview", "NotApplicable");
        Assert.Equal(0L, await fixture.Checkout.CountAsync("payment_fulfilments"));
        Assert.Equal(1L, await fixture.Checkout.CountAsync("payment_review_decisions", "disposition = 'RefundReviewPending'"));
    }

    private static Task SetRestrictionAsync(P0308Fixture fixture, Guid account, bool released) =>
        LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE public.entitlement_account_heads SET revision = revision + 1,
                released_revision = {(released ? "revision" : "NULL")}, restriction = {(released ? "'Unrestricted'" : "'Security'")}
            WHERE account_id = '{account:D}'
            """);
}
