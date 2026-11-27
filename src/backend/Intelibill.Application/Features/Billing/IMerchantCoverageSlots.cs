using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public interface IMerchantCoverageSlots
{
    Task<ErrorOr<CoverageSlotOwnerV1>> SlotConsentAsync(MerchantBillingCaller caller, Guid account, Guid shop,
        Guid assignment, ShopAffiliationConsentAction action, Guid key, CancellationToken ct);
    Task<ErrorOr<CoverageSlotTermV1>> SlotsAsync(MerchantBillingCaller caller, Guid account, Guid term, int page, CancellationToken ct);
    Task<ErrorOr<CoverageSlotAssignmentV1>> AssignAsync(MerchantBillingCaller caller, Guid account, Guid term, AssignUnusedCoverageSlotV1 command, CancellationToken ct);
    Task<ErrorOr<CoverageSlotAssignmentV1>> RecoverSlotAsync(MerchantBillingCaller caller, Guid account, Guid term, Guid key, CancellationToken ct);
}
