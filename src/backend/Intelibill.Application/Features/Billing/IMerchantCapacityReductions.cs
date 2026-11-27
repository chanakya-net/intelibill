using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public sealed record MerchantScheduledReduction(ScheduledCapacityReductionV1 Schedule, bool Created);

public interface IMerchantCapacityReductions
{
    Task<ErrorOr<CapacityReductionPreviewV1>> CreatePreviewAsync(MerchantBillingCaller caller, Guid account, Guid term,
        CreateCapacityReductionPreviewV1 request, CancellationToken ct);

    Task<ErrorOr<CapacityReductionShopPageV1>> ReadPreviewShopsAsync(MerchantBillingCaller caller, Guid account, Guid term,
        Guid previewId, Guid? after, CancellationToken ct);

    Task<ErrorOr<MerchantScheduledReduction>> ScheduleAsync(MerchantBillingCaller caller, Guid account, Guid term,
        ScheduleCapacityReductionV1 request, CancellationToken ct);

    Task<ErrorOr<ScheduledCapacityReductionV1>> ReadScheduleAsync(MerchantBillingCaller caller, Guid account, Guid term,
        CancellationToken ct);

    Task<ErrorOr<ScheduledCapacityReductionV1>> RecoverScheduleAsync(MerchantBillingCaller caller, Guid account, Guid term,
        Guid operationKey, CancellationToken ct);

    Task<ErrorOr<CapacityReductionShopPageV1>> ReadScheduleShopsAsync(MerchantBillingCaller caller, Guid account, Guid term,
        Guid scheduleId, Guid? after, CancellationToken ct);

    Task<ErrorOr<CheckoutQuoteV1>> CreateReductionOrderAsync(MerchantBillingCaller caller, Guid account, Guid term,
        Guid scheduleId, CreateReductionOrderV1 request, CancellationToken ct);

    Task<ErrorOr<ScheduledCapacityReductionV1>> ResolveScheduleAsync(MerchantBillingCaller caller, Guid account, Guid term,
        Guid scheduleId, ResolveCapacityReductionV1 request, CancellationToken ct);
}
