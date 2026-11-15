namespace Intelibill.Licensing.Domain.Entities;

public sealed class InitialAdminBootstrap
{
    public int Id { get; private set; }
    public string? ProofDigest { get; private set; }
    public string? VerifiedEmail { get; private set; }
    public string? VerificationReference { get; private set; }
    public string? AdminOrigin { get; private set; }
    public DateTimeOffset? PreparedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public Guid? OwnerId { get; private set; }
    public uint Version { get; private set; }

    public bool IsAvailable(DateTimeOffset now) => ConsumedAt is null && ExpiresAt > now;

    public void Prepare(string digest, string email, string reference, string origin, DateTimeOffset now)
    {
        ProofDigest = digest;
        VerifiedEmail = email;
        VerificationReference = reference;
        AdminOrigin = origin;
        PreparedAt = now;
        ExpiresAt = now.AddMinutes(30);
    }

    public void Consume(Guid ownerId, DateTimeOffset now)
    {
        OwnerId = ownerId;
        ConsumedAt = now;
    }
}
