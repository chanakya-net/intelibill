namespace Intelibill.Domain.Entities;

public sealed class AccountLicenseRestriction
{
    public Guid AccountId { get; init; }
    public long Revision { get; private set; }
    public DateTimeOffset UpstreamVerifiedAt { get; private set; }
    public string Restriction { get; private set; } = "";
    public long? ReleasedRevision { get; private set; }
    public Guid EvidenceId { get; private set; }

    public void Apply(long revision, DateTimeOffset verifiedAt, string restriction, long? releasedRevision, Guid evidence)
    {
        Revision = revision;
        UpstreamVerifiedAt = verifiedAt;
        Restriction = restriction;
        ReleasedRevision = releasedRevision;
        EvidenceId = evidence;
    }
}
