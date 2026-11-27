using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public interface IMerchantCoverageReplacements
{
    Task<ErrorOr<CoverageReplacementPreviewV1>> ReplacementsAsync(MerchantBillingCaller caller, Guid account, Guid term,
        int page, CancellationToken ct);

    Task<ErrorOr<CoverageReplacementV1>> ScheduleReplacementAsync(MerchantBillingCaller caller, Guid account, Guid term,
        ScheduleCoverageReplacementV1 command, CancellationToken ct);
    Task<ErrorOr<CoverageReplacementV1>> RecoverReplacementAsync(MerchantBillingCaller caller, Guid account, Guid term,
        Guid key, CancellationToken ct);

    Task<ErrorOr<ReplacementOwnerV1>> ReplacementConsentAsync(MerchantBillingCaller caller, Guid account, Guid shop,
        Guid replacement, ShopAffiliationConsentAction action, Guid key, CancellationToken ct);
}
