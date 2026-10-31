using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

/// <summary>
/// One link in a device session's chain of refresh credentials. Only a hash of the secret is
/// stored, so a copy of this table cannot be replayed. A credential renews exactly once: renewing
/// marks it consumed and links one successor, and a consumed credential presented again is treated
/// as a replay of a stolen one.
/// </summary>
/// <remarks>
/// The device session is the rotation family. A session is created by one credential check with one
/// root credential, so ending a family and ending the session are the same act.
/// </remarks>
public sealed class RefreshToken : BaseEntity
{
    public long SecurityVersion { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>
    /// The device session this credential belongs to. Null only on rows written before sessions
    /// existed; those are refused rather than adopted by a session.
    /// </summary>
    public Guid? AuthSessionId { get; private set; }

    /// <summary>The credential this one replaced; null for the first one a sign-in issued.</summary>
    public Guid? ParentTokenId { get; private set; }

    /// <summary>SHA-256 of the secret handed to the client. The secret itself is never stored.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When this credential was exchanged for its successor.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    public bool IsRevoked { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevocationReason { get; private set; }

    public User User { get; private set; } = null!;

    public bool IsConsumed => ConsumedAt is not null;

    private RefreshToken() { }

    public static RefreshToken Issue(
        Guid userId,
        Guid authSessionId,
        long securityVersion,
        string tokenHash,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return new RefreshToken
        {
            UserId = userId,
            AuthSessionId = authSessionId,
            SecurityVersion = securityVersion,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
        };
    }

    /// <summary>
    /// Usable means it may be exchanged right now: never exchanged, never revoked, and not yet at
    /// its expiry instant.
    /// </summary>
    public bool IsUsableAt(DateTimeOffset now) => !IsRevoked && ConsumedAt is null && now < ExpiresAt;

    /// <summary>
    /// Exchanges this credential for its one successor. The successor stays in the same session
    /// under the same security version; the caller has already checked both against the account.
    /// </summary>
    public RefreshToken Rotate(string successorHash, DateTimeOffset successorExpiresAt, DateTimeOffset now)
    {
        if (AuthSessionId is not Guid sessionId)
            throw new InvalidOperationException("A credential that names no session cannot be renewed.");
        if (!IsUsableAt(now))
            throw new InvalidOperationException("Only a usable credential can be renewed.");

        ConsumedAt = now;

        var successor = Issue(UserId, sessionId, SecurityVersion, successorHash, successorExpiresAt);
        successor.ParentTokenId = Id;
        return successor;
    }

    /// <summary>
    /// Ends this credential. The first reason recorded is kept: a credential already ended by a
    /// replay and later caught by a password change was already gone for the first reason.
    /// </summary>
    public void Revoke(string reason, DateTimeOffset now)
    {
        if (IsRevoked)
            return;

        IsRevoked = true;
        RevokedAt = now;
        RevocationReason = reason;
    }
}
