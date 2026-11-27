using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Trials;

public sealed record TrialExtensionShop(string ShopName, Guid AffiliationRequestId);
public sealed record TrialExtensionOverlap(Guid ShopId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record TrialExtensionReceipt(TrialExtensionResultV1 Result, Guid AdminId, Guid SessionId, string PayloadDigest);

public interface ITrialExtensionStore
{
    Task<ErrorOr<T>> InTransactionAsync<T>(Func<CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct);
    Task<(AdminIdentity? Actor, AdminSession? Session)> LockProviderAsync(AdminSessionCaller caller, CancellationToken ct);
    Task EnterAsync(Guid adminId, Guid account, CancellationToken ct);
    Task LockAccountAsync(Guid account, CancellationToken ct);
    Task<long> ReadVersionAsync(Guid account, Guid shop, CancellationToken ct);
    Task<TrialExtensionShop?> ReadShopAsync(Guid account, Guid shop, CancellationToken ct);
    Task<IReadOnlyList<TrialExtensionOverlap>> ReadOverlapsAsync(Guid account, Guid shop, TrialInterval interval, CancellationToken ct);
    Task<TrialExtensionReceipt?> ReadAsync(Guid account, Guid key, CancellationToken ct);
    Task SaveAsync(TrialExtensionReceipt receipt, DateTimeOffset previousEnd, DateTimeOffset newEnd, CancellationToken ct);
}
