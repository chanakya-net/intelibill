using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Status;

/// <summary>
/// The single evaluation and projection both merchant and provider responses share. Commercial facts are
/// projected only when the caller's authority was confirmed; nothing is cached or written back.
/// </summary>
public static class SubscriptionStatusProjection
{
    public static SubscriptionAccessFacts ToFacts(TermFactsRow row)
    {
        if (row.Basis is null)
            return new SubscriptionAccessFacts(row.StartsAt, row.EndsAt, TermEvidenceState.Incomplete);
        if (row.CalendarVersion != BillingCalendar.Version || row.AccessPolicyVersion != GracePolicy.Version
            || !Enum.TryParse<SubscriptionBasis>(row.Basis, out var basis)
            || !Enum.TryParse<BillingPeriod>(row.BillingPeriod, out var period)
            || row.AnchorLocal is not { } local || row.TimeZone is not { } zone || row.PeriodOrdinal is not { } ordinal)
            return new SubscriptionAccessFacts(row.StartsAt, row.EndsAt, TermEvidenceState.NeedsReview);

        var anchor = new BillingAnchor(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone, period);
        return new SubscriptionAccessFacts(row.StartsAt, row.EndsAt, Agrees(row, basis, anchor, ordinal)
            ? TermEvidenceState.Complete : TermEvidenceState.NeedsReview, basis, anchor, ordinal);
    }

    // A paid term is exactly one calendar period of its retained anchor; anything else needs review.
    private static bool Agrees(TermFactsRow row, SubscriptionBasis basis, BillingAnchor anchor, int ordinal)
    {
        if (ordinal is < 0 or > BillingCalendar.MaxPeriodOrdinal || !BillingCalendar.IsSupported(anchor)) return false;
        if (basis != SubscriptionBasis.Paid) return true;
        return BillingCalendar.TryBoundary(anchor, ordinal) == row.StartsAt
            && BillingCalendar.TryBoundary(anchor, ordinal + 1L) == row.EndsAt
            && BillingCalendar.TryBoundary(anchor, ordinal + 2L) is not null;
    }

    public static SubscriptionTermStatusV1 Project(TermFactsRow row, DateTimeOffset at, bool commercial)
    {
        var facts = ToFacts(row);
        var result = SubscriptionAccessPolicy.Evaluate(facts, at);
        var complete = facts.Evidence == TermEvidenceState.Complete;
        return new SubscriptionTermStatusV1(row.TermId, result.State.ToString(), facts.Evidence.ToString(),
            complete ? row.Basis : null, complete ? row.BillingPeriod : null, BillingCalendar.Zone,
            result.StartsAt, result.PaidThrough, result.GraceEndsAt, result.AccessEndsAt, result.NextDeadline,
            result.NextPeriodEndsAt, row.Mode, row.Capacity, complete ? row.MemberAllowance : null,
            row.SourceReference, row.DevelopmentFixture == true,
            commercial && row.Basis is not null ? new SubscriptionCommercialV1(row.PricePaise, row.Currency, row.PlanVersionId) : null);
    }

    public static ShopSubscriptionStatusV1 Shop(Guid shop, DateTimeOffset at, ShopCoverage? coverage, bool commercial)
    {
        var term = coverage?.Term is { } row ? Project(row, at, commercial) : null;
        return new ShopSubscriptionStatusV1(SubscriptionStatusV1.SchemaVersion, shop, at,
            term?.State ?? "PendingActivation", term, commercial ? coverage?.AccountId : null, ObserveOnly: true,
            SubscriptionStatusV1.NotAvailable, SubscriptionStatusV1.NotAvailable);
    }

    public static AccountSubscriptionStatusV1 Account(Guid account, DateTimeOffset at, IReadOnlyList<AccountTermRow> rows,
        bool commercial)
    {
        var page = rows.Take(SubscriptionStatusV1.PageSize).ToArray();
        var items = page.Select(row => new AccountTermStatusV1(Project(row.Term, at, commercial),
            row.CoveredShops, row.CoveredShopCount)).ToArray();
        return new AccountSubscriptionStatusV1(SubscriptionStatusV1.SchemaVersion, account, at, items,
            rows.Count > page.Length ? page[^1].Term.TermId : null, ObserveOnly: true,
            SubscriptionStatusV1.NotAvailable, SubscriptionStatusV1.NotAvailable);
    }

    /// <summary>PostgreSQL keeps microseconds; one evaluation instant per response, truncated to match storage.</summary>
    public static DateTimeOffset EvaluationInstant(TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
    }
}
