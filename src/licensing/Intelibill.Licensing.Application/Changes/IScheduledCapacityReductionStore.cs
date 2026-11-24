using ErrorOr;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Changes;

public sealed record StoredReductionPreview(CapacityReductionPreviewV1 Preview, Guid ActorId, string RequestHash);
public sealed record StoredScheduledReduction(ScheduledCapacityReductionV1 Schedule, Guid ActorId, string RequestHash,
    long ExpectedSourceRevision, Guid? AttemptId, Guid? ReceiptId, DateTimeOffset? PaymentEffectiveAt, int? GraceDaysIncluded);
public sealed record StoredReductionShop(Guid ShopId, string Name, Guid AffiliationRequestId, bool Retained,
    int ActiveMembers, string SeatHash);
public sealed record StoredReductionResolution(Guid ScheduleId, Guid ActorId, string RequestHash, long Generation);
public sealed record ReductionFunding(Guid AccountId, Guid ScheduleId, Guid OrderId, DateTimeOffset EffectiveAt, int GraceDaysIncluded);

public interface IMerchantReductionObserver
{
    Task<ErrorOr<IReadOnlyDictionary<Guid, ReductionObservedShopV1>>> ObserveAsync(
        Guid account, Guid term, CapacityChangeSourceShop[] shops, CancellationToken ct);
}

public interface IScheduledCapacityReductionStore
{
    Task<StoredReductionPreview?> FindPreviewByOperationAsync(Guid account, Guid operationKey, CancellationToken ct);
    Task<CapacityReductionPreviewV1?> FindPreviewAsync(Guid account, Guid term, Guid previewId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, ReductionObservedShopV1>> PreviewShopsMapAsync(Guid account, Guid previewId, CancellationToken ct);
    Task<CapacityReductionShopPageV1?> PreviewShopsPageAsync(Guid account, Guid term, Guid previewId, Guid? after, CancellationToken ct);
    Task SavePreviewAsync(CapacityReductionPreviewV1 preview, CapacityChangeSource source,
        IReadOnlyDictionary<Guid, ReductionObservedShopV1> observed, Guid actor, Guid key, string requestHash, Guid replay, CancellationToken ct);

    Task<StoredScheduledReduction?> FindScheduleByOperationAsync(Guid account, Guid operationKey, CancellationToken ct);
    Task<StoredScheduledReduction?> FindActiveScheduleAsync(Guid account, Guid term, CancellationToken ct);
    Task<StoredScheduledReduction?> FindScheduleAsync(Guid account, Guid term, Guid scheduleId, CancellationToken ct);
    Task<StoredScheduledReduction?> FindScheduleByOrderAsync(Guid account, Guid orderId, CancellationToken ct);
    Task<StoredReductionShop[]> ScheduleShopsAsync(Guid account, Guid scheduleId, CancellationToken ct);
    Task<CapacityReductionShopPageV1?> ScheduleShopsPageAsync(Guid account, Guid term, Guid scheduleId, Guid? after, CancellationToken ct);
    Task<bool> SaveScheduleAsync(ScheduledCapacityReductionV1 schedule, CapacityChangeSource source,
        IReadOnlyDictionary<Guid, ReductionObservedShopV1> observed, HashSet<Guid> retained,
        long expectedSourceRevision, Guid actor, Guid key, string requestHash, Guid replay, CancellationToken ct);

    Task<Guid?> FindReusableOrderAsync(Guid account, Guid scheduleId, DateTimeOffset now, CancellationToken ct);
    Task<bool> IsLiveOwnerAsync(Guid account, Guid actorId, CancellationToken ct);
    Task<bool> IsReductionOwnerAsync(Guid account, Guid orderId, Guid actorId, CancellationToken ct);
    Task<bool> ReductionOrderCurrentAsync(CheckoutQuoteV1 quote, DateTimeOffset now, CancellationToken ct);
    Task FundAsync(ReductionFunding funding, Guid? attemptId, Guid? receiptId, DateTimeOffset fundedAt, CancellationToken ct);
    Task<string?> OrderStateAsync(Guid account, Guid orderId, CancellationToken ct);

    Task<StoredReductionResolution?> FindResolutionByOperationAsync(Guid account, Guid operationKey, CancellationToken ct);
    Task<bool> ResolveAndQueueAsync(ScheduledCapacityReductionV1 schedule, StoredReductionShop[] shops,
        IReadOnlyDictionary<Guid, ReductionObservedShopV1> observed, Guid actor, Guid key, string requestHash,
        Guid replay, DateTimeOffset now, CancellationToken ct);
}
