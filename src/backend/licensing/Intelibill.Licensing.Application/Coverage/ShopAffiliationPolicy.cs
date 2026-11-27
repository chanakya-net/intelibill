using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Coverage;

public static class ShopAffiliationPolicy
{
    public static Error Invalid => Error.Validation("Billing.AffiliationInvalid", "Select a shop and one coverage choice.");
    public static Error Missing => Error.NotFound("Billing.AffiliationMissing", "The affiliation request was not found.");
    public static Error Conflict => Error.Conflict("Billing.AffiliationConflict", "The affiliation selection is no longer available.");

    public static bool IsExplicit(RequestShopAffiliationV1 request) =>
        request.OperationKey != Guid.Empty && request.ShopId != Guid.Empty && request.TermId != Guid.Empty &&
        request.WithoutCoverage == (request.TermId is null);
}
