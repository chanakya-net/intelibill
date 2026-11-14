namespace Intelibill.Licensing.Domain.Entities;

public sealed class BootstrapEnrollmentCredential(
    Guid id, Guid ownerId, string digest, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
{
    public Guid Id { get; private set; } = id;
    public Guid OwnerId { get; private set; } = ownerId;
    public string Digest { get; private set; } = digest;
    public DateTimeOffset IssuedAt { get; private set; } = issuedAt;
    public DateTimeOffset ExpiresAt { get; private set; } = expiresAt;
    public DateTimeOffset? RevokedAt { get; private set; }
    public uint Version { get; private set; }
}
