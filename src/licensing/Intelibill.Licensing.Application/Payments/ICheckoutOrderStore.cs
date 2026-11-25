using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed record CheckoutSourceShop(Guid ShopId, string Name, Guid RequestId, Guid? TermId,
    DateTimeOffset AppliedAt, DateTimeOffset? CoverageEndsAt);

/// <summary>
/// The latest (unsuperseded) paid period a renewal extends. <c>Mode</c>, <c>Renewed</c> (a paid renewal
/// successor or a recorded fresh start, which may still start in the future) and <c>BlocksRestoredAccess</c>
/// are deliberately outside the V1 source digest.
/// </summary>
public sealed record CheckoutRenewalSource(DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Capacity, string Basis,
    string Period, DateTime AnchorLocal, int Ordinal, string CalendarVersion, string AccessPolicyVersion, int CancellationRevision,
    string Mode = "Grouped", bool Renewed = false, long CapacityRevision = 0, Guid? ActiveReductionId = null,
    bool BlocksRestoredAccess = false, DateTimeOffset? RefundAccessEndsAt = null, long RefundRevision = 0);

public sealed record CheckoutTrialSource(Guid ShopId, Guid TermId, int Revision, DateTimeOffset EndsAt);

public sealed record CheckoutSource(CheckoutSourceShop[] Shops, CheckoutRenewalSource? Prior,
    CheckoutTrialSource[]? Trials = null)
{
    public Guid[] OrderShopIds(CoverageReplacementOrderV1? change) =>
        [.. Shops.Select(s => change is not null && s.ShopId == change.OutgoingShopId ? change.IncomingShopId : s.ShopId)];

    public CheckoutFreshStartV1 FreshStart() => new(CheckoutOrdersV1.FreshStartPolicy, Trials?.Length ?? 0,
        CheckoutOrdersV1.Digest(CheckoutOrdersV1.FreshStartPolicy, "trials",
            Trials?.Select(t => new object[] { t.ShopId, t.TermId, t.Revision, CheckoutOrdersV1.Instant(t.EndsAt) }).ToArray()
            ?? []));

    public bool Matches(CheckoutQuoteV1 quote) => Hash() == quote.SourceHash &&
        (quote.FreshStart is { } fresh ? fresh == FreshStart() : Trials is null or { Length: 0 });

    public string Hash()
    {
        var hash = OriginalHash();
        if (Prior is { CapacityRevision: > 0 } latest)
            hash = CheckoutOrdersV1.Digest(CheckoutOrdersV1.Policy, "capacity-revision", hash, latest.CapacityRevision);
        if (Prior is { ActiveReductionId: { } reductionId })
            hash = CheckoutOrdersV1.Digest(CheckoutOrdersV1.Policy, "reduction-schedule", hash, reductionId);
        if (Prior is { RefundAccessEndsAt: { } end } refunded)
            hash = CheckoutOrdersV1.Digest(CheckoutOrdersV1.Policy, "refund-boundary", hash,
                refunded.RefundRevision, CheckoutOrdersV1.Instant(end));
        return hash;
    }

    private string OriginalHash() => CheckoutOrdersV1.Digest(CheckoutOrdersV1.Policy, "source",
        Shops.Select(s => new object?[] { s.ShopId, s.Name, s.RequestId, s.TermId, CheckoutOrdersV1.Instant(s.AppliedAt),
            s.CoverageEndsAt is { } end ? CheckoutOrdersV1.Instant(end) : null }).ToArray(),
        Prior is { } p ? new object[] { CheckoutOrdersV1.Instant(p.StartsAt), CheckoutOrdersV1.Instant(p.EndsAt),
            p.Capacity, p.Basis, p.Period, p.AnchorLocal, p.Ordinal, p.CalendarVersion, p.AccessPolicyVersion,
            p.CancellationRevision } : null);
}

public sealed record StoredCheckoutQuote(CheckoutQuoteV1 Quote, Guid ActorId, string RequestHash);
public sealed record StoredCheckoutAcceptance(Guid OrderId, Guid ActorId, string RequestHash);

public interface ICheckoutOrderStore
{
    Task<CoverageReplacementOrderV1?> ReplacementAsync(Guid account, Guid term, int capacity, int allowance,
        DateTimeOffset boundary, DateTimeOffset now, CancellationToken ct);
    Task<bool> IncreaseStillCurrentAsync(CheckoutQuoteV1 quote, DateTimeOffset now, CancellationToken ct);
    Task<bool> IsIncreaseOwnerAsync(Guid account, Guid order, Guid actor, CancellationToken ct);
    Task<bool> ReductionOrderCurrentAsync(CheckoutQuoteV1 quote, DateTimeOffset now, CancellationToken ct);
    Task<bool> IsReductionOwnerAsync(Guid account, Guid order, Guid actor, CancellationToken ct);
    Task<bool> HasConflictingPaidCoverageAsync(Guid account, Guid[] shops, Guid? excludeTerm, DateTimeOffset startsAt,
        DateTimeOffset endsAt, CancellationToken ct);
    Task<StoredCheckoutQuote?> FindAsync(Guid account, Guid id, bool byOperation, CancellationToken ct);
    Task<CheckoutSource?> SourceAsync(Guid account, Guid? term, Guid[] requested, CancellationToken ct);
    Task SaveAsync(CheckoutQuoteV1 quote, CheckoutSource source, Guid actor, Guid key, string requestHash, Guid replay, CancellationToken ct);
    Task<CheckoutShopPageV1> PageAsync(Guid account, Guid? order, Guid? after, CancellationToken ct);
    Task<ReviewSourceShop[]> ReviewShopsAsync(Guid account, Guid order, CancellationToken ct);
    Task<ReviewCancellationWithdrawal[]> WithdrawnRevisionsAsync(Guid account, Guid term, CancellationToken ct);
    Task<Guid[]> ShopIdsAsync(Guid account, Guid order, CancellationToken ct);
    Task<StoredCheckoutAcceptance?> FindAcceptanceAsync(Guid account, Guid key, CancellationToken ct);
    Task<int> AcceptAsync(CheckoutQuoteV1 quote, CheckoutAcceptanceV1 acceptance, Guid actor, Guid request, Guid replay,
        string requestHash, CancellationToken ct);
}
