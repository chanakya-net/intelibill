using Intelibill.Domain.Common;
namespace Intelibill.Domain.Entities;

public sealed class EmailOwnershipChallenge : BaseEntity
{
    public string Purpose { get; private set; } = string.Empty;
    public string Destination { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public Guid? UserId { get; private set; }
    public long SecurityVersion { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    private EmailOwnershipChallenge() { }
    public static EmailOwnershipChallenge Create(string purpose, string destination, string hash,
        DateTimeOffset expiresAt, Guid? userId = null, long version = 0) => new()
        { Purpose = purpose, Destination = destination, TokenHash = hash, ExpiresAt = expiresAt, UserId = userId, SecurityVersion = version };
    public void Consume(DateTimeOffset now) => ConsumedAt = now;
}
