using System.Net;
using System.Net.Http.Json;
using System.Text;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0608Tests
{
    [Fact]
    public async Task WrongAccountNonverifiedAndInvalidAmountsAreRejected()
    {
        await using var fixture = await StartAsync(database, RequestedInstant);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (purchase, term) = await PurchaseAsync(package, "Month");
        var confirmation = await ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);

        // Second order on the same account with a started but uncaptured attempt (no payment confirmation).
        var secondQuote = await AcceptAsync(package, await RenewalQuoteAsync(package, term, "Month"));
        var uncapturedAttempt = await StartPaymentAsync(package, secondQuote);

        // Second billing account owned by the same payer, plus an unrelated outsider.
        var otherAccount = await P0209Fixture.CreateAccountAsync(package.Payer.Client, "Other Account");
        var stranger = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);

        // Wrong account and unverified/mismatched payment references are indistinguishable 404s.
        foreach (var (account, order, confirm) in new[]
        {
            (otherAccount, purchase.OrderId, confirmation),
            (package.Account, secondQuote.OrderId, uncapturedAttempt),
            (package.Account, secondQuote.OrderId, confirmation),
            (package.Account, purchase.OrderId, Guid.NewGuid()),
        })
        {
            using var missing = await PostRefundAsync(package.Payer.Client, account,
                RequestBody(order, confirm, 1000L, "Valid reason for lookup check"));
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal(RefundRequestsV1.PaymentNotFound, await P0206Fixture.CodeAsync(missing));
        }

        using var forbidden = await PostRefundAsync(stranger.Client, package.Account,
            RequestBody(purchase.OrderId, confirmation, 1000L, "Outsider attempt"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Invalid amounts: zero, negative, above verified receipt, above MaxPaise, and fractional JSON.
        foreach (var badAmount in new[] { 0L, -1L, 12001L, CheckoutOrdersV1.MaxPaise + 1 })
        {
            using var bad = await PostRefundAsync(package.Payer.Client, package.Account,
                RequestBody(purchase.OrderId, confirmation, badAmount, "Amount boundary check"));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal(RefundRequestsV1.AmountInvalid, await P0206Fixture.CodeAsync(bad));
        }

        using var fractionalContent = new StringContent(
            $$"""{"operationKey":"{{Guid.NewGuid():D}}","orderId":"{{purchase.OrderId:D}}","confirmationId":"{{confirmation:D}}","amountPaise":12.5,"reason":"Fractional check"}""",
            Encoding.UTF8, "application/json");
        using var fractional = await package.Payer.Client.PostAsync(RefundsPath(package.Account), fractionalContent);
        Assert.Equal(HttpStatusCode.BadRequest, fractional.StatusCode);

        // Invalid reasons and empty identities.
        foreach (var badReason in new[] { "", "   ", new string('x', RefundRequestsV1.MaxReasonLength + 1), "line1\nline2" })
        {
            using var bad = await PostRefundAsync(package.Payer.Client, package.Account,
                RequestBody(purchase.OrderId, confirmation, 1000L, badReason));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal(RefundRequestsV1.Invalid, await P0206Fixture.CodeAsync(bad));
        }

        foreach (var body in new[]
        {
            RequestBody(purchase.OrderId, confirmation, 1000L, "Empty key", Guid.Empty),
            RequestBody(Guid.Empty, confirmation, 1000L, "Empty order"),
            RequestBody(purchase.OrderId, Guid.Empty, 1000L, "Empty confirmation"),
        })
        {
            using var bad = await PostRefundAsync(package.Payer.Client, package.Account, body);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal(RefundRequestsV1.Invalid, await P0206Fixture.CodeAsync(bad));
        }

        Assert.Equal(0L, await fixture.CountAsync("refund_requests"));
    }

    [Fact]
    public async Task DatabaseRefusesIncoherentRequestRows()
    {
        await using var fixture = await StartAsync(database, RequestedInstant);
        var package = await fixture.PackageAsync(1, ["Guard Shop"]);
        var (purchase, _) = await PurchaseAsync(package, "Month");
        var confirmation = await ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        var attempt = (Guid)(await fixture.ScalarAsync(
            $"SELECT attempt_id FROM public.payment_confirmations WHERE confirmation_id = '{confirmation:D}'"))!;
        var providerId = (string)(await fixture.ScalarAsync(
            $"SELECT facts->>'providerId' FROM public.payment_confirmations WHERE confirmation_id = '{confirmation:D}'"))!;
        var providerAccount = (string)(await fixture.ScalarAsync(
            $"SELECT facts->>'providerAccountId' FROM public.payment_confirmations WHERE confirmation_id = '{confirmation:D}'"))!;
        var environment = (string)(await fixture.ScalarAsync(
            $"SELECT facts->>'environment' FROM public.payment_confirmations WHERE confirmation_id = '{confirmation:D}'"))!;
        var configVersion = (string)(await fixture.ScalarAsync(
            $"SELECT facts->>'configurationVersion' FROM public.payment_confirmations WHERE confirmation_id = '{confirmation:D}'"))!;

        var cases = new (long Amount, long PaymentAmount, string Provider, string Account, string Env, string Config, Guid? Attempt)[]
        {
            (12001L, 12000L, providerId, providerAccount, environment, configVersion, attempt),
            (1000L, 12001L, providerId, providerAccount, environment, configVersion, attempt),
            (1000L, 12000L, "forged-provider", providerAccount, environment, configVersion, attempt),
            (1000L, 12000L, providerId, "forged-account", environment, configVersion, attempt),
            (1000L, 12000L, providerId, providerAccount, "production", configVersion, attempt),
            (1000L, 12000L, providerId, providerAccount, environment, "forged-config", attempt),
            (1000L, 12000L, providerId, providerAccount, environment, configVersion, Guid.NewGuid()),
        };

        foreach (var bad in cases)
        {
            var sql = $"""
                BEGIN;
                SELECT set_config('app.billing_account_id', '{package.Account:D}', true);
                INSERT INTO public.refund_requests (account_id, refund_request_id, order_id, confirmation_id,
                    attempt_id, manual_receipt_id, actor_id, operation_key, request_hash, replay_id,
                    provider_id, provider_account_id, environment, configuration_version,
                    payment_amount_paise, amount_paise, currency, reason, requested_at)
                VALUES ('{package.Account:D}', '{Guid.NewGuid():D}', '{purchase.OrderId:D}', '{confirmation:D}',
                    '{bad.Attempt:D}', NULL, '{package.Payer.UserId:D}', '{Guid.NewGuid():D}', '{new string('a', 64)}',
                    '{Guid.NewGuid():D}', '{bad.Provider}', '{bad.Account}', '{bad.Env}', '{bad.Config}',
                    {bad.PaymentAmount}, {bad.Amount}, 'INR', 'Direct insert guard check', now());
                COMMIT;
                """;
            var refused = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, sql));
            Assert.Equal("23514", refused.SqlState);
        }

        Assert.Equal(0L, await fixture.CountAsync("refund_requests"));
    }

    [Fact]
    public async Task ManualVerifiedPaymentConfirmationSupportsRefundRequest()
    {
        var clock = new P0201Clock { Now = P0302Fixture.WholeSecondStart };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, P0306Tests.Settings);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await P0306Tests.SignInBillingAsync(fixture);
        using var receipt = await P0212Fixture.PostAsync(billing, P0306Tests.Path(purchase.Quote),
            P0306Tests.Receipt(purchase.Quote, clock.Now));
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);

        var confirmation = await ConfirmationIdAsync(purchase.Payer.Client, purchase.Account, purchase.Quote.OrderId);
        using var response = await PostRefundAsync(purchase.Payer.Client, purchase.Account,
            RequestBody(purchase.Quote.OrderId, confirmation, purchase.Quote.AmountPaise, "Manual receipt refund request"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1L, await fixture.CountAsync("refund_requests", "manual_receipt_id IS NOT NULL AND attempt_id IS NULL"));
    }
}
