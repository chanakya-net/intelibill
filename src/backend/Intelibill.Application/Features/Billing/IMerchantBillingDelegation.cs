using ErrorOr;
using Intelibill.Application.Common.Security;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

/// <summary>The live verified merchant session a delegation is issued for, with its access-token expiry.</summary>
public sealed record MerchantBillingCaller(SessionClaims Session, long AccessExpiry);

public sealed record MerchantBillingAccessRequest(Guid AccountId, SessionClaims Session, long AccessExpiry);

public sealed record MerchantCreateBillingAccount(Guid OperationKey, string Name);

public sealed record MerchantCreatedBillingAccount(BillingAccountV1 Account, bool Created);

public interface IMerchantBillingAccess
{
    Task<ErrorOr<BillingAccountAccessV1>> GetAsync(MerchantBillingAccessRequest access, CancellationToken cancellationToken);

    Task<ErrorOr<BillingAccountListV1>> ListAccountsAsync(MerchantBillingCaller caller, CancellationToken cancellationToken);

    Task<ErrorOr<MerchantCreatedBillingAccount>> CreateAccountAsync(MerchantBillingCaller caller,
        MerchantCreateBillingAccount request, CancellationToken cancellationToken);
}

public static class MerchantBillingErrors
{
    public static Error Unavailable => Error.Failure("Billing.DelegationUnavailable", "Billing access is temporarily unavailable.");
    public static Error Denied => Error.Forbidden("Billing.AccountAccessDenied", "Billing account access was denied.");
    public static Error SessionInvalid => Error.Unauthorized("Billing.SessionInvalid", "A live verified merchant session is required.");
    public static Error NameInvalid => Error.Validation("Billing.AccountNameInvalid",
        $"Enter a billing account name of 1 to {MerchantBillingDelegationV1.MaximumAccountNameLength} characters.");
    public static Error OperationKeyInvalid => Error.Validation("Billing.OperationKeyInvalid", "The operation key is invalid.");
    public static Error PayloadMismatch => Error.Conflict("Billing.OperationPayloadMismatch",
        "This operation key was already used with different details.");
}
