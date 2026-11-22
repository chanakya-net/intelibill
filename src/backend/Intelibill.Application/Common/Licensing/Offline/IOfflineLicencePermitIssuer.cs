using ErrorOr;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Common.Licensing.Offline;

/// <summary>
/// Server-side issuance of offline licence evidence. Nothing here is reachable from a request body:
/// callers pass the authenticated session and server-made identifiers, and every deadline is read
/// from stored facts.
/// </summary>
public interface IOfflineLicencePermitIssuer
{
    /// <summary>
    /// Freezes the verified entitlement for a lease that is being reserved. A participant: it runs
    /// inside the reservation's own read-committed transaction, before the lease is saved, stages one
    /// grant row for that same save and never commits. A lease that already exists is refused.
    /// </summary>
    Task<ErrorOr<OfflineGrantEvidence>> CaptureGrantAsync(SessionClaims? session, InvoiceLease lease, CancellationToken ct);

    /// <summary>
    /// Signs and stores the permit for a completed server snapshot, in its own transaction. Repeating
    /// the same completion returns the stored permit after authority is checked again.
    /// </summary>
    Task<ErrorOr<OfflinePermitEvidence>> IssueAsync(OfflinePermitRequest request, CancellationToken ct);

    /// <summary>
    /// Proves a permit against this shop's retained public keys, the scope the caller expects and the
    /// instant the caller is judging. Runs inside the caller's shop-scoped connection.
    /// </summary>
    Task<ErrorOr<OfflineLicensePermitV1>> VerifyAsync(string? signedPermit, OfflinePermitScope expected,
        DateTimeOffset at, CancellationToken ct);
}

/// <summary>
/// What the server's own snapshot stream knows when it finishes: who it captured for and how many
/// lots it emitted. It carries no timestamp or deadline; those come from the clock and retained rows.
/// </summary>
public sealed record OfflineSnapshotCompletion(Guid SnapshotId, Guid ShopId, Guid UserId, Guid SessionId,
    string DeviceId, Guid LeaseId, long CatalogVersion, int BatchCount);

public sealed record OfflinePermitRequest(SessionClaims? Session, Guid GrantId, OfflineSnapshotCompletion Completion,
    Guid? TillSessionId = null);

public sealed record OfflinePermitScope(Guid ShopId, Guid UserId, Guid SessionId, string DeviceId, Guid LeaseId);

public sealed record OfflineGrantEvidence(Guid GrantId, string KeyId, string SignedGrant)
{
    public override string ToString() => $"grant {GrantId:D}";
}

public sealed record OfflinePermitEvidence(Guid PermitId, string KeyId, string SignedPermit, DateTimeOffset NotBefore,
    DateTimeOffset ValidUntil, string LimitingReason)
{
    public override string ToString() => $"permit {PermitId:D}";
}

/// <summary>Signs with the dedicated offline-permit key and proves the result against its pinned public half.</summary>
public interface IOfflinePermitSigner
{
    /// <summary>Null when no usable, purpose-exclusive key is configured or the signature does not verify.</summary>
    Task<OfflinePermitSignature?> SignAsync(string type, byte[] payload, CancellationToken ct);
}

public sealed record OfflinePermitSignature(string KeyId, byte[] PublicKey, string Token)
{
    public override string ToString() => $"signature by {KeyId}";
}

/// <summary>Live identity facts read under the actor row lock.</summary>
public sealed record OfflineAuthFacts(long MembershipAuthorityVersion, long SessionScopeVersion, DateTimeOffset AuthDeadline);

/// <summary>What the server retained for one snapshot's emitted lots.</summary>
public sealed record OfflineSnapshotFacts(int BatchCount, long? CatalogVersion, int CatalogVersions,
    DateTimeOffset? FirstCapturedAt, DateTimeOffset? LastCapturedAt, DateTimeOffset? EarliestExpiry);

/// <summary>Tenant-scoped reads and append-only writes behind the issuer.</summary>
public interface IOfflineLicenceEvidenceStore
{
    /// <summary>True only for a lease this unit of work is about to insert.</summary>
    bool IsBeingReserved(InvoiceLease lease);

    /// <summary>Null unless the user is an Active Owner or Manager with this live, unrevoked session.</summary>
    Task<OfflineAuthFacts?> ReadAuthAsync(Guid shopId, Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken ct);

    void StageGrant(OfflineLicenseGrant grant);
    Task<OfflineLicenseGrant?> FindGrantAsync(Guid shopId, Guid grantId, CancellationToken ct);
    Task<InvoiceLease?> FindLeaseAsync(Guid shopId, Guid leaseId, CancellationToken ct);
    Task<OfflineSnapshotFacts> ReadSnapshotAsync(Guid shopId, Guid snapshotId, CancellationToken ct);
    Task<OfflineLicensePermit?> FindPermitAsync(Guid shopId, Guid snapshotId, CancellationToken ct);

    /// <summary>Keeps the public key for (shop, key id); refuses an identifier already bound to other material.</summary>
    Task<ErrorOr<Success>> RetainKeyAsync(Guid shopId, string keyId, byte[] publicKey, DateTimeOffset now, CancellationToken ct);

    Task<byte[]?> FindKeyAsync(Guid shopId, string keyId, CancellationToken ct);

    /// <summary>False when another writer already stored a permit for this snapshot.</summary>
    Task<bool> TryInsertPermitAsync(OfflineLicensePermit permit, CancellationToken ct);
}
