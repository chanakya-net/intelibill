namespace Intelibill.Domain.Entities;

/// <summary>
/// The verified entitlement one new invoice lease was reserved under. Written once, in the
/// reservation's own transaction, and never replaced: a lease without one cannot get offline permission.
/// </summary>
public sealed class OfflineLicenseGrant
{
    public Guid Id { get; init; }
    public Guid ShopId { get; init; }
    public Guid LeaseId { get; init; }
    public Guid AccountId { get; init; }
    public Guid AffiliationRequestId { get; init; }
    public long AccountRevision { get; init; }
    public long ShopRevision { get; init; }
    public Guid AccountEvidenceId { get; init; }
    public Guid ShopEvidenceId { get; init; }
    public DateTimeOffset AuthorityVerifiedAt { get; init; }
    public DateTimeOffset AuthorityValidUntil { get; init; }
    public Guid UserId { get; init; }
    public Guid SessionId { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public long MembershipAuthorityVersion { get; init; }
    public long SessionScopeVersion { get; init; }
    public DateTimeOffset DeviceVerifiedAt { get; init; }
    public DateTimeOffset AuthDeadline { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public string KeyId { get; init; } = string.Empty;

    /// <summary>The exact signed bytes; the columns above only index them.</summary>
    public string SignedGrant { get; init; } = string.Empty;
}

/// <summary>One signed offline permit: a grant, a completed server snapshot and the window they allow.</summary>
public sealed class OfflineLicensePermit
{
    public Guid Id { get; init; }
    public Guid ShopId { get; init; }
    public Guid GrantId { get; init; }
    public Guid LeaseId { get; init; }
    public Guid SnapshotId { get; init; }
    public Guid UserId { get; init; }
    public Guid SessionId { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public Guid? TillSessionId { get; init; }
    public int BatchCount { get; init; }
    public long CatalogVersion { get; init; }
    public long CurrentAccountRevision { get; init; }
    public long CurrentShopRevision { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public DateTimeOffset NotBefore { get; init; }
    public DateTimeOffset ValidUntil { get; init; }
    public string LimitingReason { get; init; } = string.Empty;
    public string KeyId { get; init; } = string.Empty;
    public string SignedPermit { get; init; } = string.Empty;
}

/// <summary>
/// Public half of a key that signed this shop's offline evidence, kept after rotation so old
/// evidence stays provable. Private material is never stored here.
/// </summary>
public sealed class OfflineLicenseVerificationKey
{
    public Guid Id { get; init; }
    public Guid ShopId { get; init; }
    public string KeyId { get; init; } = string.Empty;

    /// <summary>DER SubjectPublicKeyInfo.</summary>
    public byte[] PublicKey { get; init; } = [];

    /// <summary>Lowercase SHA-256 of <see cref="PublicKey"/>.</summary>
    public string Fingerprint { get; init; } = string.Empty;
    public DateTimeOffset FirstUsedAt { get; init; }
}
