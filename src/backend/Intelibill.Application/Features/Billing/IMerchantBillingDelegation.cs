using ErrorOr;
using Intelibill.Application.Common.Security;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public sealed record MerchantBillingAccessRequest(Guid AccountId, SessionClaims Session, long AccessExpiry);

public interface IMerchantBillingAccess
{
    Task<ErrorOr<BillingAccountAccessV1>> GetAsync(MerchantBillingAccessRequest access, CancellationToken cancellationToken);
}

public static class MerchantBillingErrors
{
    public static Error Unavailable => Error.Failure("Billing.DelegationUnavailable", "Billing access is temporarily unavailable.");
    public static Error Denied => Error.Forbidden("Billing.AccountAccessDenied", "Billing account access was denied.");
    public static Error SessionInvalid => Error.Unauthorized("Billing.SessionInvalid", "A live verified merchant session is required.");
}
