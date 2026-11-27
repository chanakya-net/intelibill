using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Billing;

/// <summary>
/// A verified merchant assertion. <see cref="AccountId"/> is present for account-scoped actions and absent
/// for actor-scoped actions (<see cref="MerchantBillingDelegationV1.IsActorScoped"/>).
/// </summary>
public sealed record VerifiedBillingDelegation(
    Guid ActorId, Guid? AccountId, string Action, Guid RequestId, Guid ReplayId,
    string KeyId, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt,
    string Method, string Path, string BodySha256);

/// <summary>The open delegation transaction an operation runs in. Context comes only from server state.</summary>
public interface IBillingDelegationScope
{
    Guid ActorId { get; }

    /// <summary>
    /// Narrow row-level security to one account for the rest of the transaction. Only actor-scoped
    /// delegations may choose the account, and only from values the server derived or read under RLS.
    /// </summary>
    Task UseAccountAsync(Guid accountId, CancellationToken cancellationToken);
}

public interface IBillingDelegationAccess
{
    Task<ErrorOr<BillingAccountAccessV1>> AuthorizeAsync(VerifiedBillingDelegation delegation, CancellationToken cancellationToken);

    /// <summary>
    /// Authorize, consume the replay identifier and run <paramref name="operation"/> in one transaction.
    /// An error from the operation rolls back everything, the replay use included.
    /// </summary>
    Task<ErrorOr<T>> ExecuteAsync<T>(VerifiedBillingDelegation delegation,
        Func<IBillingDelegationScope, CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken cancellationToken);

    /// <summary>
    /// As <see cref="ExecuteAsync{T}"/>, for work that binds two billing accounts. The counterparty is resolved
    /// from the signed request or the scoped proposal, and both account locks are taken in canonical order before
    /// the live membership row is read, so pair work can never deadlock or decide on a stale owner.
    /// </summary>
    Task<ErrorOr<T>> ExecutePairAsync<T>(VerifiedBillingDelegation delegation,
        Func<CancellationToken, Task<Guid?>> counterparty,
        Func<IBillingDelegationScope, CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken cancellationToken);
}
