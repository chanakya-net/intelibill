using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public interface IMerchantCoverageReplacementExceptions
{
    Task<ErrorOr<CoverageReplacementExceptionPreviewV1>> CoverageExceptionsAsync(MerchantBillingCaller caller, Guid account,
        Guid term, int page, CancellationToken ct);

    Task<ErrorOr<CoverageReplacementExceptionV1>> RequestCoverageExceptionAsync(MerchantBillingCaller caller, Guid account,
        Guid term, RequestCoverageReplacementExceptionV1 command, CancellationToken ct);

    Task<ErrorOr<CoverageReplacementExceptionV1>> RecoverCoverageExceptionAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid key, CancellationToken ct);

    Task<ErrorOr<CoverageReplacementExceptionOwnerV1>> CoverageExceptionConsentAsync(MerchantBillingCaller caller, Guid account,
        Guid shop, Guid exceptionId, ShopAffiliationConsentAction action, Guid key, CancellationToken ct);
}
