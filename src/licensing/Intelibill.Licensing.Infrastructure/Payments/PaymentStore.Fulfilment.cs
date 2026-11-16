using System.Globalization;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class PaymentStore
{
    public Task RecordVerifiedAsync(StoredPaymentAttempt attempt, PaymentObservation p, DateTimeOffset at, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.verified_payments (attempt_id, account_id, order_id, provider_id, provider_account_id,
                environment, configuration_version, provider_payment_reference, amount_paise, currency, captured_at, verified_at)
            VALUES ({attempt.AttemptId}, {attempt.AccountId}, {attempt.OrderId}, {p.Pin.ProviderId}, {p.Pin.ProviderAccountId},
                {p.Pin.Environment}, {p.Pin.ConfigurationVersion}, {p.ProviderPaymentReference}, {p.AmountPaise}, {p.Currency},
                {p.CapturedAt}, {at})
            """, ct);

    /// <summary>Fulfilment, paid term, frozen facts, shop links, audit and per-shop entitlement snapshots together.</summary>
    public async Task FulfilAsync(PaymentFulfilment f, CancellationToken ct)
    {
        var (attempt, term) = (f.Attempt, f.Term);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_fulfilments (account_id, order_id, attempt_id, term_id, policy_version, fulfilled_at)
            VALUES ({attempt.AccountId}, {attempt.OrderId}, {attempt.AttemptId}, {term.TermId},
                {PaymentFulfilmentPolicy.Version}, {f.At})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.subscription_coverage_terms (term_id, account_id, mode, capacity, starts_at, ends_at)
            VALUES ({term.TermId}, {attempt.AccountId}, {term.Mode}, {term.Capacity}, {term.StartsAt}, {term.EndsAt})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.subscription_term_facts (term_id, account_id, basis, billing_period, anchor_local, time_zone,
                period_ordinal, calendar_version, access_policy_version, member_allowance, price_paise, currency,
                plan_version_id, source_reference, development_fixture, recorded_at)
            VALUES ({term.TermId}, {attempt.AccountId}, 'Paid', {term.Period}, {term.AnchorLocal.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}::timestamp,
                {BillingCalendar.Zone}, {term.Ordinal}, {BillingCalendar.Version}, {GracePolicy.Version}, {f.MemberAllowance},
                {attempt.AmountPaise}, {attempt.Currency}, {f.PlanVersionId}, {PaymentFulfilmentPolicy.Version},
                {f.DevelopmentFixture}, {f.At})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_fulfilment_shops (account_id, order_id, term_id, shop_id, affiliation_request_id)
            SELECT account_id, {attempt.OrderId}, {term.TermId}, shop_id, request_id FROM public.shop_affiliations
            WHERE account_id = {attempt.AccountId} AND shop_id = ANY({f.ShopIds})
            """, ct);
        await AppendEventAsync(attempt, PaymentEventKinds.OrderFulfilled, null, f.At, ct);
        await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE", ct);
        foreach (var shop in f.ShopIds.Order())
            await publisher.PublishAsync(attempt.AccountId, shop, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_fulfilment_deliveries (account_id, order_id, shop_id, shop_revision)
            SELECT account_id, {attempt.OrderId}, shop_id, revision FROM public.entitlement_shop_heads
            WHERE account_id = {attempt.AccountId} AND shop_id = ANY({f.ShopIds})
            """, ct);
    }
}
