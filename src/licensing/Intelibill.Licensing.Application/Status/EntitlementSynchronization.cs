using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Status;

public interface IEntitlementSynchronizationReader
{
    Task<IReadOnlyDictionary<Guid, EntitlementSynchronizationV1>> ReadAsync(Guid account, Guid[] shops, CancellationToken ct,
        Guid? closure = null);
}

internal static class EntitlementSynchronization
{
    internal static Task<IReadOnlyDictionary<Guid, EntitlementSynchronizationV1>> ReadPageAsync(
        this IEntitlementSynchronizationReader reader, Guid account, IReadOnlyList<AccountTermRow> rows, CancellationToken ct) =>
        reader.ReadAsync(account, [.. rows.Take(SubscriptionStatusV1.PageSize).SelectMany(row => row.CoveredShops)
            .Select(shop => shop.ShopId).Distinct()], ct);
}
