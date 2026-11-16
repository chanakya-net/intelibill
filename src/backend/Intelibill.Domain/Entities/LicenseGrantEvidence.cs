namespace Intelibill.Domain.Entities;

/// <summary>Original bytes, verification material and local receipt time for one accepted service event.</summary>
public sealed class LicenseGrantEvidence
{
    public Guid EventId { get; init; }
    public Guid AccountId { get; init; }
    public Guid ShopId { get; init; }
    public Guid AffiliationRequestId { get; init; }
    public long? AccountRevision { get; init; }
    public long? ShopRevision { get; init; }
    public DateTimeOffset? AccountUpstreamVerifiedAt { get; init; }
    public DateTimeOffset? ShopUpstreamVerifiedAt { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
    public DateTimeOffset AppliedAt { get; init; }
    public string KeyId { get; init; } = "";
    public string PublicKey { get; init; } = "";
    public string Assertion { get; init; } = "";
    public string Digest { get; init; } = "";
    public byte[] Payload { get; init; } = [];
}
