namespace Intelibill.Licensing.Domain.Entities;

/// <summary>
/// One numbered version of the trial policy. The trial lasts a fixed 14 calendar days; its caps have no
/// unlimited value and stay missing until a manager records them.
/// </summary>
public sealed class TrialPolicyVersion
{
    public const int TrialDurationDays = 14;

    private TrialPolicyVersion(Guid id, int versionNumber, DateTimeOffset createdAt, Guid createdByAdminId)
    {
        Id = id;
        VersionNumber = versionNumber;
        Status = CatalogueVersionStatus.Draft;
        DurationDays = TrialDurationDays;
        CreatedAt = createdAt;
        CreatedByAdminId = createdByAdminId;
    }

    public Guid Id { get; private set; }
    public int VersionNumber { get; private set; }
    public CatalogueVersionStatus Status { get; private set; }
    public int Revision { get; private set; }
    public CatalogueSource Source { get; private set; }
    public Guid? SourceVersionId { get; private set; }
    public int? SourceExpectedVersion { get; private set; }
    public int DurationDays { get; private set; }
    public int? ActiveMemberCap { get; private set; }
    public int? MaxConcurrentTrialShops { get; private set; }
    public string? TrialTermsVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAdminId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid UpdatedByAdminId { get; private set; }

    public TrialPolicyFacts Facts => new(ActiveMemberCap, MaxConcurrentTrialShops, TrialTermsVersion);

    public static TrialPolicyVersion Draft(Guid id, int versionNumber, TrialPolicyFacts facts, Guid adminId,
        DateTimeOffset now, CatalogueSource source = CatalogueSource.Unknown, Guid? sourceVersionId = null, int? sourceExpectedVersion = null)
    {
        var version = new TrialPolicyVersion(id, versionNumber, now, adminId) { Source = source, SourceVersionId = sourceVersionId, SourceExpectedVersion = sourceExpectedVersion };
        version.Revise(facts, adminId, now);
        return version;
    }

    public void Revise(TrialPolicyFacts facts, Guid adminId, DateTimeOffset now)
    {
        if (Status != CatalogueVersionStatus.Draft)
            throw new InvalidOperationException("Only a draft trial policy version can be revised.");
        ActiveMemberCap = facts.ActiveMemberCap;
        MaxConcurrentTrialShops = facts.MaxConcurrentTrialShops;
        TrialTermsVersion = facts.TrialTermsVersion;
        Revision++;
        UpdatedAt = now;
        UpdatedByAdminId = adminId;
    }

    public void Publish(Guid adminId, DateTimeOffset now) =>
        Transition(CatalogueVersionStatus.Draft, CatalogueVersionStatus.Published, adminId, now);

    public void Retire(Guid adminId, DateTimeOffset now) =>
        Transition(CatalogueVersionStatus.Published, CatalogueVersionStatus.Retired, adminId, now);

    private void Transition(CatalogueVersionStatus from, CatalogueVersionStatus to, Guid adminId, DateTimeOffset now)
    {
        if (Status != from) throw new InvalidOperationException("Invalid catalogue lifecycle transition.");
        Status = to;
        Revision++;
        UpdatedAt = now;
        UpdatedByAdminId = adminId;
    }
}
