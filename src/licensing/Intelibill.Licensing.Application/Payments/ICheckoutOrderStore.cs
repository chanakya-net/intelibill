using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed record CheckoutSourceShop(Guid ShopId, string Name, Guid RequestId, Guid? TermId,
    DateTimeOffset AppliedAt, DateTimeOffset? CoverageEndsAt);

/// <summary>
/// The latest (unsuperseded) paid period a renewal extends. <c>Mode</c> and <c>Renewed</c> (itself a paid renewal
/// successor, which may still start in the future) are deliberately outside the V1 source digest.
/// </summary>
public sealed record CheckoutRenewalSource(DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Capacity, string Basis,
    string Period, DateTime AnchorLocal, int Ordinal, string CalendarVersion, string AccessPolicyVersion, int CancellationRevision,
    string Mode = "Grouped", bool Renewed = false);

public sealed record CheckoutSource(CheckoutSourceShop[] Shops, CheckoutRenewalSource? Prior)
{
    public string Hash() => CheckoutOrdersV1.Digest(CheckoutOrdersV1.Policy, "source",
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
    Task<StoredCheckoutQuote?> FindAsync(Guid account, Guid id, bool byOperation, CancellationToken ct);
    Task<CheckoutSource?> SourceAsync(Guid account, Guid? term, Guid[] requested, CancellationToken ct);
    Task SaveAsync(CheckoutQuoteV1 quote, CheckoutSource source, Guid actor, Guid key, string requestHash, Guid replay, CancellationToken ct);
    Task<CheckoutShopPageV1> PageAsync(Guid account, Guid? order, Guid? after, CancellationToken ct);
    Task<Guid[]> ShopIdsAsync(Guid account, Guid order, CancellationToken ct);
    Task<StoredCheckoutAcceptance?> FindAcceptanceAsync(Guid account, Guid key, CancellationToken ct);
    Task<int> AcceptAsync(CheckoutQuoteV1 quote, CheckoutAcceptanceV1 acceptance, Guid actor, Guid request, Guid replay,
        string requestHash, CancellationToken ct);
}
