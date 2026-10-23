using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

/// <summary>
/// One transaction boundary for every shop-authority transition: role and status changes,
/// invitation issue, revocation and acceptance.
/// </summary>
/// <remarks>
/// Callers acquire locks in a single order — the relevant <c>users</c> rows in ascending id order,
/// then the <c>shops</c> root, then the membership and invitation rows in ascending id order — so
/// concurrent transitions on the same shop serialise instead of deadlocking, and every authority
/// decision is re-read after the locks are held rather than trusted from a request claim.
/// Plan 40's ownership transfer joins the same protocol.
/// </remarks>
public interface IMembershipAuthorityRepository
{
    Task<IAsyncDisposable> BeginAsync(CancellationToken ct);

    /// <summary>Locks the given accounts in ascending id order. Missing ids are ignored.</summary>
    Task LockUsersAsync(IReadOnlyList<Guid> userIds, CancellationToken ct);

    /// <summary>Locks the shop root. False when no such shop exists.</summary>
    Task<bool> LockShopAsync(Guid shopId, CancellationToken ct);

    /// <summary>
    /// Loads one membership for update, with its account attached. Returns rows in every status so
    /// callers can distinguish "never a member" from "suspended" or "removed".
    /// </summary>
    Task<ShopMembership?> LockMembershipAsync(Guid shopId, Guid userId, CancellationToken ct);

    Task<User?> FindUserAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Reads one invitation without locking, so a caller can learn which accounts and shop the
    /// authority-lock protocol has to cover before it starts taking locks.
    /// </summary>
    Task<ShopInvitation?> PeekInvitationAsync(Guid invitationId, CancellationToken ct);

    Task<ShopInvitation?> LockInvitationAsync(Guid invitationId, CancellationToken ct);

    Task<IReadOnlyList<ShopInvitation>> GetInvitationsForShopAsync(Guid shopId, CancellationToken ct);

    /// <summary>
    /// Voids every still-pending invitation to one shop, optionally narrowed to one address. Used
    /// when a new invitation supersedes an old one and when an authority change invalidates grants
    /// issued under the previous authority.
    /// </summary>
    Task RevokePendingInvitationsAsync(Guid shopId, string? email, DateTimeOffset now, CancellationToken ct);

    /// <summary>Voids pending invitations issued by one administrator for one shop.</summary>
    Task RevokeInvitationsFromInviterAsync(Guid shopId, Guid inviterUserId, DateTimeOffset now, CancellationToken ct);

    Task<int> CountRecentInvitationsAsync(Guid shopId, DateTimeOffset since, CancellationToken ct);

    void AddInvitation(ShopInvitation invitation);

    Task AddMembershipAsync(ShopMembership membership, CancellationToken ct);

    /// <summary>Commits the transaction. False when a concurrent writer won the race.</summary>
    Task<bool> CommitAsync(CancellationToken ct);
}
