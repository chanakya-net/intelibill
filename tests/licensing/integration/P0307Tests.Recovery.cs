using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0307Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PaidRevisionConflictRecoversOnlyOriginalAcceptedTerms(bool manual)
    {
        await using var fixture = await P0308Fixture.StartAsync(database, new DateTimeOffset(2027, 1, 31, 4, 30, 0, TimeSpan.Zero), P0306Tests.Settings);
        var held = await HoldRenewalAsync(fixture, manual);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        if (manual)
        {
            using var receipt = await P0212Fixture.PostAsync(finance, P0306Tests.Path(held.Quote),
                P0306Tests.Receipt(held.Quote, fixture.Checkout.CheckoutClock.Now));
            Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        }
        else
        {
            await CaptureAsync(held.Package.Payer.Client, held.Quote, held.Attempt!.Value);
            await VerifyAsync(held.Package.Payer.Client, held.Quote, held.Attempt.Value);
        }

        var pending = await StatusAsync(held.Package.Payer.Client, held.Quote);
        AssertStatus(pending, "Verified", "NeedsReview", "NotApplicable");
        AssertContractShape(pending, "heldCheckout");
        var review = await SingleCaseAsync(finance, held.Package.Account);
        Assert.Equal("CoverageChanged", review.GetProperty("classification").GetString());
        var path = $"{ReviewPath(held.Package.Account)}/{review.GetProperty("caseId").GetGuid():D}";
        using var blocked = await finance.SendAsync(HttpMethod.Get, path);
        var before = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CoverageChanged", before.GetProperty("recoveryBlocker").GetString());
        await WithdrawAsync(held);

        var command = await RecoveryCommandAsync(finance, path);
        if (!manual)
            command = await ExerciseRecoveryFencesAsync(fixture, held, finance, path, command);
        using var accepted = await P0212Fixture.PostAsync(finance, path + "/decisions", command);
        Assert.True(accepted.StatusCode == HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
        var applied = await StatusAsync(held.Package.Payer.Client, held.Quote);
        AssertStatus(applied, "Verified", "Fulfilled", "Applying");
        AssertContractShape(applied, "recoveredCheckout");
        Assert.Equal(held.Quote.PreviewStartsAt, applied.GetProperty("termStartsAt").GetDateTimeOffset());
        Assert.Equal(held.Quote.PreviewEndsAt, applied.GetProperty("termEndsAt").GetDateTimeOffset());
        Assert.Equal(manual ? 0L : 1L, await fixture.Checkout.CountAsync("verified_payments"));
        Assert.Equal(manual ? 1L : 0L, await fixture.Checkout.CountAsync("manual_payment_receipts"));
        Assert.Equal(1L, await fixture.Checkout.CountAsync("payment_fulfilments"));
    }

    [Fact]
    public async Task CoverageHoldRetainsVerifiedPaymentWithSlightProviderClockSkew()
    {
        var probe = new P0302Tests.GatewayProbe();
        await using var fixture = await P0308Fixture.StartAsync(database, P0210Fixture.Start, P0306Tests.Settings,
            services: probe.Install);
        probe.Database = fixture.Hosts.LicensingOwner;
        var held = await HoldRenewalAsync(fixture, manual: false);
        probe.Account = held.Package.Account;
        var attempt = held.Attempt!.Value;
        await CaptureAsync(held.Package.Payer.Client, held.Quote, attempt);
        var verifiedAt = fixture.Checkout.CheckoutClock.Now;
        var future = verifiedAt.AddSeconds(1);
        probe.Tamper = payment => payment with { AmountPaise = payment.AmountPaise - 1 };
        AssertStatus(await VerifyAsync(held.Package.Payer.Client, held.Quote, attempt), "PendingVerification", "NotFulfilled", "NotApplicable");
        probe.Tamper = payment => payment with { CapturedAt = future, CreatedAt = future };
        AssertStatus(await VerifyAsync(held.Package.Payer.Client, held.Quote, attempt), "Verified", "NeedsReview", "NotApplicable");
        Assert.Equal(1L, await fixture.Checkout.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.Checkout.CountAsync("payment_fulfilments"));
        Assert.Equal(1L, await fixture.Checkout.CountAsync("unmatched_payments", "classification = 'Underpayment'"));
        Assert.Equal(1L, await fixture.Checkout.CountAsync("unmatched_payments", "classification = 'CoverageChanged'"));

        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        using var cases = await finance.SendAsync(HttpMethod.Get, ReviewPath(held.Package.Account));
        Assert.Equal(HttpStatusCode.OK, cases.StatusCode);
        var rows = await cases.Content.ReadFromJsonAsync<JsonElement>();
        var review = Assert.Single(rows.EnumerateArray(), row => row.GetProperty("classification").GetString() == "CoverageChanged");
        Assert.Equal(future, review.GetProperty("capturedAt").GetDateTimeOffset());
        Assert.Equal(future, review.GetProperty("createdAt").GetDateTimeOffset());
        var path = $"{ReviewPath(held.Package.Account)}/{review.GetProperty("caseId").GetGuid():D}";
        await WithdrawAsync(held);
        using var blocked = await finance.SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
        var preview = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CaptureTimeUnproven", preview.GetProperty("recoveryBlocker").GetString());
        Assert.Equal(verifiedAt.ToUnixTimeMilliseconds(),
            preview.GetProperty("references").GetProperty("verifiedAt").GetDateTimeOffset().ToUnixTimeMilliseconds());

        fixture.At(future);
        var command = await RecoveryCommandAsync(finance, path);
        using var accepted = await P0212Fixture.PostAsync(finance, path + "/decisions", command);
        Assert.True(accepted.StatusCode == HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
        Assert.Equal(1L, await fixture.Checkout.CountAsync("verified_payments"));
        Assert.Equal(1L, await fixture.Checkout.CountAsync("payment_fulfilments"));
    }

    private static async Task<ReviewPaymentCommand> ExerciseRecoveryFencesAsync(P0308Fixture fixture, HeldRenewal held,
        P0110CookieBrowser finance, string path, ReviewPaymentCommand command)
    {
        fixture.At(fixture.Checkout.CheckoutClock.Now.AddSeconds(1));
        var again = await ScheduleAsync(held.Package, held.Prior);
        using var changed = await P0212Fixture.PostAsync(finance, path + "/decisions", command);
        Assert.Equal("PaymentReview.RecoveryUnavailable", await P0206Fixture.CodeAsync(changed));
        await WithdrawAsync(held with { Cancellation = again });
        using var stale = await P0212Fixture.PostAsync(finance, path + "/decisions", command);
        Assert.Equal("PaymentReview.CaseVersionConflict", await P0206Fixture.CodeAsync(stale));

        // Recovery after the grace boundary still uses the already recorded, pre-boundary capture.
        fixture.At(held.Quote.PreviewStartsAt.AddDays(8));
        var refreshed = await RecoveryCommandAsync(finance, path);
        await AssertPublicationRollbackAsync(fixture, finance, path, refreshed);
        return refreshed;
    }

    private static async Task AssertPublicationRollbackAsync(P0308Fixture fixture, P0110CookieBrowser finance, string path,
        ReviewPaymentCommand command)
    {
        var owner = fixture.Hosts.LicensingOwner;
        await LicensingPostgreSqlFixture.ExecuteAsync(owner, "REVOKE INSERT ON public.entitlement_snapshots FROM licensing_runtime");
        try
        {
            using var failed = await P0212Fixture.PostAsync(finance, path + "/decisions", command);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Equal(1L, await fixture.Checkout.CountAsync("verified_payments"));
            Assert.Equal(1L, await fixture.Checkout.CountAsync("unmatched_payments"));
            Assert.Equal(0L, await fixture.Checkout.CountAsync("payment_review_decisions"));
            Assert.Equal(0L, await fixture.Checkout.CountAsync("payment_fulfilments"));
        }
        finally
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(owner, "GRANT INSERT ON public.entitlement_snapshots TO licensing_runtime");
        }
    }

    private static async Task<ReviewPaymentCommand> RecoveryCommandAsync(P0110CookieBrowser finance, string path)
    {
        using var preview = await finance.SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var detail = await preview.Content.ReadFromJsonAsync<JsonElement>();
        var hash = detail.GetProperty("recoverySourceHash").GetString();
        Assert.NotNull(hash);
        return new(Guid.NewGuid(), 0, "RecoverAcceptedTerms", "Cancellation withdrawn on the original package",
            "FIXTURE-ORIGINAL-RECEIPT", hash);
    }

    private static void AssertContractShape(JsonElement checkout, string phase)
    {
        foreach (var field in ReviewContract().GetProperty(phase).EnumerateObject())
            Assert.Equal(field.Value.GetRawText(), checkout.GetProperty(field.Name).GetRawText());
    }

    internal static async Task<HeldRenewal> HoldRenewalAsync(P0308Fixture fixture, bool manual)
    {
        // Keep the imported term active on both the fixed settlement clock and the consent transport clock.
        var offset = TimeSpan.FromMinutes(330);
        var consentNow = fixture.Checkout.Delivery.AuthClock.GetUtcNow().ToOffset(offset);
        var anchor = new DateTimeOffset(consentNow.Year, consentNow.Month, 1, 0, 0, 0, offset);
        var start = anchor.ToUniversalTime();
        var end = anchor.AddMonths(1).ToUniversalTime();
        fixture.At(consentNow.ToUniversalTime().AddSeconds(2));
        var payer = await P0209Fixture.SignInAsync(fixture.Hosts, Intelibill.Domain.Enums.ShopRole.Owner);
        var account = await P0209Fixture.CreateAccountAsync(payer.Client);
        var prior = await P0209Fixture.SeedTermAsync(fixture.Hosts, account,
            new(start, end, AnchorLocal: anchor.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Grouped: true, Capacity: 3));
        var second = await P0209Fixture.SignInAsync(fixture.Hosts, Intelibill.Domain.Enums.ShopRole.Owner);
        foreach (var shop in new[] { payer, second })
            await P0213Fixture.LinkAsync(account, payer, shop, prior);
        await fixture.Checkout.Delivery.DrainAsync(source: true);
        await fixture.Checkout.Delivery.DrainAsync(source: false);
        var plan = await P0301Tests.PublishPlanAsync(fixture.Checkout.Trial);
        var package = new P0308Fixture.Package(payer, account, [payer, second], plan);
        var quote = await P0308Fixture.AcceptAsync(package, await P0308Fixture.RenewalQuoteAsync(package, prior, "Month"));
        Guid? attempt = manual ? null : await P0308Fixture.StartPaymentAsync(package, quote);
        var cancellation = await ScheduleAsync(package, prior);
        return new(package, quote, prior, attempt, cancellation);
    }

    private static async Task<JsonElement> ScheduleAsync(P0308Fixture.Package package, Guid prior)
    {
        var path = P0215Tests.CancellationPath(package.Account, prior);
        var preview = await P0215Fixture.PreviewAsync(package.Payer, path);
        using var response = await package.Payer.Client.PostAsJsonAsync(path, P0215Tests.Accept(preview, Guid.NewGuid()));
        return (await P0209Fixture.ReadAsync(response)).GetProperty("cancellation");
    }

    internal static async Task WithdrawAsync(HeldRenewal held)
    {
        using var response = await held.Package.Payer.Client.PostAsJsonAsync(
            P0216Tests.WithdrawalPath(held.Package.Account, held.Prior), P0216Tests.Withdraw(held.Cancellation, Guid.NewGuid()));
        await P0209Fixture.ReadAsync(response);
    }

    internal sealed record HeldRenewal(P0308Fixture.Package Package, CheckoutQuoteV1 Quote, Guid Prior, Guid? Attempt,
        JsonElement Cancellation);
}
