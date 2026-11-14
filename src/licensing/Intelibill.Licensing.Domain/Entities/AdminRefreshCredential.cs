namespace Intelibill.Licensing.Domain.Entities;

/// <summary>
/// One link of a session's single-use refresh chain. Only the SHA-256 digest of the presented
/// value is stored; each successor names the credential it replaced.
/// </summary>
public sealed class AdminRefreshCredential(
    Guid id, Guid sessionId, Guid? parentId, string tokenHash, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
{
    public Guid Id { get; private set; } = id;
    public Guid SessionId { get; private set; } = sessionId;
    public Guid? ParentId { get; private set; } = parentId;
    public string TokenHash { get; private set; } = tokenHash;
    public DateTimeOffset IssuedAt { get; private set; } = issuedAt;
    public DateTimeOffset ExpiresAt { get; private set; } = expiresAt;
    public DateTimeOffset? ConsumedAt { get; private set; }

    public bool IsConsumed => ConsumedAt is not null;

    public void Consume(DateTimeOffset now) => ConsumedAt ??= now;
}
