using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Subscriptions;

public sealed record CapacityChangeSourceShop(Guid ShopId, string Name, Guid RequestId);

/// <summary>
/// The purchased facts of one account term as recorded at fulfilment: never the live catalogue. <c>Superseded</c>
/// marks a term another paid fulfilment already extended; <c>Trial</c> a trial term; <c>Suspended</c> an effective
/// account or covered-shop suspension; <c>Closed</c> a covered shop under an unreopened closure.
/// </summary>
public sealed record CapacityChangeSource(Guid TermId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Capacity,
    string? Basis, string? Period, DateTime? AnchorLocal, int? Ordinal, string? CalendarVersion, int? MemberAllowance,
    long? PricePaise, string? Currency, Guid? PlanVersionId, bool DevelopmentFixture, bool Trial, bool Superseded,
    int CancellationRevision, bool Suspended, bool Closed, CapacityChangeSourceShop[] Shops)
{
    public string Hash() => CheckoutOrdersV1.Digest(CapacityChangeQuotesV1.Policy, "source", TermId,
        CheckoutOrdersV1.Instant(StartsAt), CheckoutOrdersV1.Instant(EndsAt), Capacity, Basis, Period, AnchorLocal, Ordinal,
        CalendarVersion, MemberAllowance, PricePaise, Currency, PlanVersionId, Trial, CancellationRevision,
        Shops.Select(s => new object[] { s.ShopId, s.Name, s.RequestId }).ToArray());
}

public sealed record StoredCapacityChangeQuote(CapacityChangeQuoteV1 Quote, Guid ActorId, string RequestHash);

public interface ICapacityChangeQuoteStore
{
    Task<StoredCapacityChangeQuote?> FindAsync(Guid account, Guid operationKey, CancellationToken ct);
    Task<CapacityChangeSource?> SourceAsync(Guid account, Guid term, DateTimeOffset now, CancellationToken ct);
    Task SaveAsync(CapacityChangeQuoteV1 quote, CapacityChangeSource source, PaymentProviderPin? pin, long? routingRevision,
        Guid actor, Guid key, string requestHash, Guid replay, CancellationToken ct);
    Task<CapacityChangeShopPageV1?> PageAsync(Guid account, Guid term, Guid quote, Guid? after, CancellationToken ct);
}
