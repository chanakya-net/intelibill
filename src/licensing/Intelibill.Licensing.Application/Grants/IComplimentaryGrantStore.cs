using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Grants;

/// <summary>A named shop's current affiliation with the account (the identity a grant link freezes).</summary>
public sealed record GrantShopSource(Guid ShopId, string ShopName, Guid AffiliationRequestId);

/// <summary>An existing complimentary window for a shop.</summary>
public sealed record GrantWindow(Guid ShopId, Guid GrantId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>A committed grant with its provider receipt, enough to replay the original outcome exactly.</summary>
public sealed record StoredComplimentaryGrant(Guid GrantId, Guid TermId, Guid AccountId, Guid OperationKey, Guid AdminId,
    Guid SessionId, ComplimentaryGrantFacts Facts, long ExpectedVersion, string SourceHash, string PayloadDigest,
    DateTimeOffset RecordedAt, IReadOnlyList<GrantShopSource> Shops, Guid? PaidTermId = null);

/// <summary>
/// Persistence for complimentary grants. Every call runs inside <see cref="InTransactionAsync{T}"/>; the provider
/// identity and session are locked first, then the billing account fence, then the account's grant scope.
/// </summary>
public interface IComplimentaryGrantStore
{
    Task<ErrorOr<T>> InTransactionAsync<T>(Func<CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct);

    Task<(AdminIdentity? Actor, AdminSession? Session)> LockProviderAsync(AdminSessionCaller caller, CancellationToken ct);

    /// <summary>Transaction-local provider grant context bound to one account.</summary>
    Task EnterAsync(Guid adminId, Guid account, CancellationToken ct);

    /// <summary>The account's advisory fence, shared with trial activation and affiliation.</summary>
    Task LockAccountAsync(Guid account, CancellationToken ct);

    /// <summary>The account's grant revision (0 when it has never had a grant).</summary>
    Task<long> ReadRevisionAsync(Guid account, CancellationToken ct);

    Task<IReadOnlyList<GrantShopSource>> ReadShopsAsync(Guid account, Guid[] shops, CancellationToken ct);

    Task<Guid?> ReadPaidTermAsync(Guid account, Guid order, Guid[] shops, CancellationToken ct);

    Task<IReadOnlyList<GrantWindow>> ReadWindowsAsync(Guid account, Guid[] shops, CancellationToken ct);

    Task<StoredComplimentaryGrant?> ReadAsync(Guid account, Guid operationKey, CancellationToken ct);

    Task SaveAsync(StoredComplimentaryGrant grant, CancellationToken ct);
}
