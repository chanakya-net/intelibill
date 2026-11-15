using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.BillingAccounts;

public sealed record StoredBillingAccount(
    Guid Id, string Name, Guid CreatedByMerchantUserId, byte[] CreationRequestHash, DateTimeOffset CreatedAt,
    BillingAccountRole Role);

/// <summary>
/// Billing account persistence inside the open delegation transaction. Every read is filtered by
/// row-level security for the scope's actor and account.
/// </summary>
public interface IBillingAccountStore
{
    /// <summary>Insert the account and its owner unless the creation fence already holds the account.</summary>
    Task<bool> TryCreateAsync(BillingAccount account, BillingAccountMember owner, CancellationToken cancellationToken);

    /// <summary>The account with the actor's active role, or null when it is not visible to the actor.</summary>
    Task<StoredBillingAccount?> FindAsync(Guid accountId, Guid actorId, CancellationToken cancellationToken);

    /// <summary>The actor's own active memberships, oldest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<Guid>> ListActorAccountIdsAsync(Guid actorId, int limit, CancellationToken cancellationToken);
}
