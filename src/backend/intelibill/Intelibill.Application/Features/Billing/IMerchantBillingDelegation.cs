using ErrorOr;
using Intelibill.Application.Common.Security;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Billing;

/// <summary>The live verified merchant session a delegation is issued for, with its access-token expiry.</summary>
public sealed record MerchantBillingCaller(SessionClaims Session, long AccessExpiry);

public sealed record MerchantBillingAccessRequest(Guid AccountId, SessionClaims Session, long AccessExpiry);

public sealed record MerchantCreateBillingAccount(Guid OperationKey, string Name);

public sealed record MerchantCreatedBillingAccount(BillingAccountV1 Account, bool Created);

public interface IMerchantBillingAccess
{
    Task<ErrorOr<BillingAccountAccessV1>> GetAsync(MerchantBillingAccessRequest access, CancellationToken cancellationToken);

    Task<ErrorOr<BillingAccountListV1>> ListAccountsAsync(MerchantBillingCaller caller, Guid? after, CancellationToken cancellationToken);

    Task<ErrorOr<PublishedCatalogueV1>> ListPlansAsync(MerchantBillingCaller caller, Guid? after, CancellationToken cancellationToken);

    Task<ErrorOr<PublishedPlanVersionV1>> ReadPlanVersionAsync(MerchantBillingCaller caller, Guid versionId,
        CancellationToken cancellationToken);

    Task<ErrorOr<MerchantCreatedBillingAccount>> CreateAccountAsync(MerchantBillingCaller caller,
        MerchantCreateBillingAccount request, CancellationToken cancellationToken);
}

public static class MerchantBillingErrors
{
    public static Error PlanVersionNotFound => Errors.Billing.PlanVersionNotFound;
    public static Error Unavailable => Errors.Billing.DelegationUnavailable;
    public static Error Denied => Errors.Billing.AccountAccessDenied;
    public static Error SessionInvalid => Errors.Billing.SessionInvalid;
    public static Error NameInvalid => Error.Validation("Billing.AccountNameInvalid",
        $"Enter a billing account name of 1 to {MerchantBillingDelegationV1.MaximumAccountNameLength} characters.");
    public static Error OperationKeyInvalid => Errors.Billing.OperationKeyInvalid;
    public static Error PayloadMismatch => Errors.Billing.OperationPayloadMismatch;
}
