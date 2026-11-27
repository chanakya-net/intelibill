using ErrorOr;

namespace Intelibill.Licensing.Application.Billing;

public interface IBillingAccountDelegationAuthority
{
    /// <summary>
    /// Check the current actor, account and action using the caller's scoped licensing DbContext.
    /// Hold the membership lock in its existing transaction until the caller commits or rolls back.
    /// The account and actor candidate scope is already set for RLS. Missing authority must fail closed.
    /// </summary>
    Task<ErrorOr<Success>> AuthorizeAsync(Guid accountId, Guid actorId, string action, CancellationToken cancellationToken);
}

public static class BillingDelegationErrors
{
    public static Error Invalid => Error.Unauthorized("Billing.DelegationInvalid", "The billing delegation is invalid.");
    public static Error Replayed => Error.Unauthorized("Billing.DelegationReplayed", "The billing delegation has already been used.");
    public static Error Denied => Error.Forbidden("Billing.AccountAccessDenied", "Billing account access was denied.");
    public static Error MembershipUnavailable => Error.Failure("Billing.MembershipUnavailable", "Billing membership is unavailable.");
    public static Error StorageUnavailable => Error.Failure("Billing.DelegationUnavailable", "Billing access is temporarily unavailable.");
}
