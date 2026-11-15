namespace Intelibill.Licensing.Domain.Entities;

/// <summary>
/// One numbered version of a plan. While a draft it is revised in place (each save is one revision);
/// it is never offered, priced to a customer or enforced.
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
        DateTimeOffset now)
    {
        var version = new PlanVersion(id, planId, versionNumber, now, adminId);
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
}
