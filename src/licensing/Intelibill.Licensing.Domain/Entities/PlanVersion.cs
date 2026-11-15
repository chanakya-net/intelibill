namespace Intelibill.Licensing.Domain.Entities;

/// <summary>
/// One numbered plan version. Draft saves advance the revision, and publication freezes the commercial facts.
/// </summary>
public sealed class PlanVersion
{
    public const string Currency = "INR";

    private PlanVersion(Guid id, Guid planId, int versionNumber, DateTimeOffset createdAt, Guid createdByAdminId)
    {
        Id = id;
        PlanId = planId;
        VersionNumber = versionNumber;
        Status = CatalogueVersionStatus.Draft;
        CurrencyCode = Currency;
        CreatedAt = createdAt;
        CreatedByAdminId = createdByAdminId;
        Name = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid PlanId { get; private set; }
    public int VersionNumber { get; private set; }
    public CatalogueVersionStatus Status { get; private set; }
    public int Revision { get; private set; }
    public CatalogueSource Source { get; private set; }
    public Guid? SourceVersionId { get; private set; }
    public int? SourceExpectedVersion { get; private set; }
    public string Name { get; private set; }
    public string CurrencyCode { get; private set; }
    public long? MonthlyPricePaise { get; private set; }
    public long? AnnualPricePaise { get; private set; }
    public int? IncludedShops { get; private set; }
    public int? MembersPerShop { get; private set; }
    public bool? ExtraShopsOffered { get; private set; }
    public long? ExtraShopMonthlyPricePaise { get; private set; }
    public long? ExtraShopAnnualPricePaise { get; private set; }
    public string? CustomerTermsVersion { get; private set; }
    public string? RefundPolicyVersion { get; private set; }
    public string? NegotiationReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAdminId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid UpdatedByAdminId { get; private set; }

    public PlanDraftFacts Facts => new(Name, MonthlyPricePaise, AnnualPricePaise, IncludedShops, MembersPerShop,
        ExtraShopsOffered, ExtraShopMonthlyPricePaise, ExtraShopAnnualPricePaise, CustomerTermsVersion,
        RefundPolicyVersion, NegotiationReason);

    public static PlanVersion Draft(Guid id, Guid planId, int versionNumber, PlanDraftFacts facts, Guid adminId,
        DateTimeOffset now, CatalogueSource source = CatalogueSource.Unknown, Guid? sourceVersionId = null, int? sourceExpectedVersion = null)
    {
        var version = new PlanVersion(id, planId, versionNumber, now, adminId) { Source = source, SourceVersionId = sourceVersionId, SourceExpectedVersion = sourceExpectedVersion };
        version.Revise(facts, adminId, now);
        return version;
    }

    /// <summary>Replace every fact of this draft (a full replacement) and advance its revision.</summary>
    public void Revise(PlanDraftFacts facts, Guid adminId, DateTimeOffset now)
    {
        if (Status != CatalogueVersionStatus.Draft)
            throw new InvalidOperationException("Only a draft plan version can be revised.");
        Name = facts.Name;
        MonthlyPricePaise = facts.MonthlyPricePaise;
        AnnualPricePaise = facts.AnnualPricePaise;
        IncludedShops = facts.IncludedShops;
        MembersPerShop = facts.MembersPerShop;
        ExtraShopsOffered = facts.ExtraShopsOffered;
        ExtraShopMonthlyPricePaise = facts.ExtraShopMonthlyPricePaise;
        ExtraShopAnnualPricePaise = facts.ExtraShopAnnualPricePaise;
        CustomerTermsVersion = facts.CustomerTermsVersion;
        RefundPolicyVersion = facts.RefundPolicyVersion;
        NegotiationReason = facts.NegotiationReason;
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
