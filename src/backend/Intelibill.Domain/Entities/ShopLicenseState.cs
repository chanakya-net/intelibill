namespace Intelibill.Domain.Entities;

public sealed class ShopLicenseState
{
    public Guid ShopId { get; init; }
    public Guid AccountId { get; init; }
    public Guid AffiliationRequestId { get; init; }
    public DateTimeOffset? EnforcementActivatedAt { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset UpstreamVerifiedAt { get; private set; }
    public string State { get; private set; } = "";
    public Guid? TermId { get; private set; }
    public string? Basis { get; private set; }
    public DateTimeOffset? StartsAt { get; private set; }
    public DateTimeOffset? AccessEndsAt { get; private set; }
    public DateTimeOffset? NextBoundary { get; private set; }
    public int? ShopCapacity { get; private set; }
    public int? MemberAllowance { get; private set; }
    public string? SuspensionMode { get; private set; }
    public DateTimeOffset? SuspensionEffectiveAt { get; private set; }
    public string? SuspensionExplanation { get; private set; }
    public long? ReleasedRevision { get; private set; }
    public long? ReopenedRevision { get; private set; }
    public long? ClosureRevision { get; private set; }
    public Guid EvidenceId { get; private set; }

    public void Apply(long revision, DateTimeOffset verifiedAt, string state, Guid? termId, string? basis,
        DateTimeOffset? startsAt, DateTimeOffset? endsAt, DateTimeOffset? nextBoundary, int? capacity, int? members, Guid evidence,
        string? suspensionMode = null, DateTimeOffset? suspensionEffectiveAt = null, string? suspensionExplanation = null,
        long? releasedRevision = null, long? reopenedRevision = null, long? closureRevision = null)
    {
        Revision = revision;
        UpstreamVerifiedAt = verifiedAt;
        State = state;
        TermId = termId;
        Basis = basis;
        StartsAt = startsAt;
        AccessEndsAt = endsAt;
        NextBoundary = nextBoundary;
        ShopCapacity = capacity;
        MemberAllowance = members;
        SuspensionMode = suspensionMode;
        SuspensionEffectiveAt = suspensionEffectiveAt;
        SuspensionExplanation = suspensionExplanation;
        ReleasedRevision = releasedRevision;
        ReopenedRevision = reopenedRevision;
        ClosureRevision = closureRevision;
        EvidenceId = evidence;
    }
}
