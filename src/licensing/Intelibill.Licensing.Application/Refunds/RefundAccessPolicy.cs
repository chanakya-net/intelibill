using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Refunds;

/// <summary>
/// Pure derivation of one explicit treatment's effect on the term a payment funded. Preserve names the term and
/// shops and changes nothing. EndAt and TermAdjustment apply only to a complete, unrenewed, uncommitted paid term at a
/// future storage-precision instant inside its current access; nothing is inferred from the refunded amount.
/// </summary>
public static class RefundAccessPolicy
{
    public static RefundAccessImpact? Impact(RefundAccessTarget? target, string treatment, DateTimeOffset? effectiveAt,
        DateTimeOffset now)
    {
        var shops = target?.Shops.Select(shop => new RefundAccessShop(shop.ShopId, shop.ShopName, shop.Restricted,
            shop.ScheduledRestrictionAt, shop.Closed, shop.CourtesyEndsAt)).ToArray() ?? [];
        var facts = target is null ? null : SubscriptionStatusProjection.ToFacts(target.Term);
        // Evaluated at the decision instant; the boundaries themselves do not depend on it.
        var current = facts is { Evidence: TermEvidenceState.Complete, Basis: SubscriptionBasis.Paid }
            ? SubscriptionAccessPolicy.Evaluate(facts, now) : null;
        if (treatment == RefundApprovalRules.Preserve)
            return effectiveAt is null ? new(treatment, target?.Term.TermId, shops, null, current?.PaidThrough,
                current?.AccessEndsAt, current?.PaidThrough, current?.AccessEndsAt) : null;

        if (target is null || current is not { AccessEndsAt: { } accessEnds } || target.Committed || shops.Length == 0
            || effectiveAt is not { } requested || requested != BillingAccountRules.StorageInstant(requested))
            return null;
        var at = requested.ToUniversalTime();
        var ends = treatment == RefundApprovalRules.EndAt;
        if (at <= now || at <= target.Term.StartsAt || (ends ? at > accessEnds : at >= current.PaidThrough))
            return null;
        // Judged against the access already in force: EndAt keeps nothing after its instant, grace included; a term
        // adjustment replaces paid-through and an eligible term keeps its ordinary grace, never past the current end.
        var graced = at + GracePolicy.EligiblePaidGrace;
        var (paid, until) = ends ? (at < current.PaidThrough ? at : current.PaidThrough, at)
            : (at, current.GraceEndsAt is null ? at : graced < accessEnds ? graced : accessEnds);
        return new(treatment, target.Term.TermId, shops, at, current.PaidThrough, accessEnds, paid, until);
    }

    /// <summary>Binds the reviewed preview: the payment pin, policy, treatment, instants and the live named scope.</summary>
    public static string Digest(RefundApprovalSource source, RefundAccessTarget? target, RefundAccessImpact access,
        long amount, RefundReceiptLedger ledger) =>
        CheckoutOrdersV1.Digest(RefundApprovalRules.Policy, "impact", source.RefundRequestId, source.ConfirmationId,
            amount, ledger.Revision, ledger.OccupiedPaise, source.PaymentAmountPaise - ledger.OccupiedPaise,
            source.PaymentAmountPaise, source.RequestedAmountPaise, source.ProviderId, source.ProviderAccountId,
            source.Environment, source.ConfigurationVersion, source.RefundPolicyVersion, access.Treatment, access.TermId,
            target?.TermRevision, Instant(access.EffectiveAt), Instant(access.PreviousPaidThrough),
            Instant(access.PreviousAccessEndsAt), Instant(access.NewPaidThrough), Instant(access.NewAccessEndsAt),
            target?.Shops.Select(shop => new object?[] { shop.ShopId, shop.RequestId, shop.ShopName, shop.Restricted,
                Instant(shop.ScheduledRestrictionAt), shop.Closed, Instant(shop.CourtesyEndsAt) }).ToArray());

    private static string? Instant(DateTimeOffset? value) => value is { } at ? CheckoutOrdersV1.Instant(at) : null;
}
