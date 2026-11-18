using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Restrictions;

public interface ISuspensionStore
{
    Task<CustomerSuspensionV1?> ReadCustomerAsync(Guid account, Guid shop, DateTimeOffset at, CancellationToken ct);
    Task LockAccountAsync(Guid account, CancellationToken ct);
    Task<string?> ReadAccountNameAsync(Guid account, CancellationToken ct);
    Task<IReadOnlyList<SuspensionShop>> ReadShopsAsync(Guid account, Guid? shop, CancellationToken ct);
    Task<(long Version, bool Active)> ReadVersionAsync(Guid account, Guid? shop, CancellationToken ct);
    Task<SavedSuspension?> ReadOperationAsync(Guid account, Guid key, CancellationToken ct);
    Task<SavedSuspension?> ReadAsync(Guid account, Guid id, CancellationToken ct);
    Task<IReadOnlyList<SavedSuspension>> ListAsync(Guid account, CancellationToken ct);
    Task SaveAsync(SavedSuspension suspension, IReadOnlyList<SuspensionShop> shops, CancellationToken ct);
    Task<IReadOnlyList<SuspensionDelivery>> ReadDeliveryAsync(Guid id, CancellationToken ct);
}
