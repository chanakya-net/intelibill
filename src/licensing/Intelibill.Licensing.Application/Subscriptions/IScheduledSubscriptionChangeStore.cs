using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Status;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Subscriptions;

public sealed record CancellationShop(Guid ShopId, Guid RequestId, string ShopName, DateTimeOffset AppliedAt, DateTimeOffset? CoverageEndsAt);
public sealed record CancellationReceipt(Guid ActorId, string PayloadHash, SubscriptionCancellationOutcomeV1 Outcome);

public interface IScheduledSubscriptionChangeStore
{
    Task<TermFactsRow?> ReadTermAsync(Guid account, Guid term, CancellationToken ct);
    Task<CancellationShop[]> ReadAssignedShopsAsync(Guid account, Guid term, CancellationToken ct);
    Task<CancellationShopV1[]> ReadFrozenShopsAsync(Guid account, Guid change, Guid? after, CancellationToken ct);
    Task<CancellationReceipt?> FindReceiptAsync(Guid account, Guid key, CancellationToken ct);
    Task AppendAsync(VerifiedBillingDelegation proof, CancellationSource source, Guid change, Guid operationKey,
        string payloadHash, DateTimeOffset at, CancellationToken ct);
}

public sealed record CancellationSource(TermFactsRow Term, CancellationShop[] Shops, string ShopHash, string SourceHash);
