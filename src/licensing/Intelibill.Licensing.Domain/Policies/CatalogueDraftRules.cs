using System.Text.RegularExpressions;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Domain.Policies;

public enum CatalogueRuleViolation
{
    None = 0,
    NameInvalid,
    AmountInvalid,
    CapacityInvalid,
    ExtraShopTermsInvalid,
    PolicyVersionInvalid,
    CustomReasonRequired,
    ReasonNotAllowed,
}

/// <summary>
/// The single place that decides what a draft may hold and what it still lacks. Hard rules refuse a save;
/// missing facts are saved as missing and listed, so an incomplete draft can never read as unlimited.
/// </summary>
public static partial class CatalogueDraftRules
{
    public const long MaxPaise = 1_000_000_000_000;
    public const int MinCapacity = 1;
    public const int MaxCapacity = 100_000;
    public const int MaxNameLength = 80;
    public const int MaxReasonLength = 500;

    public static CatalogueRuleViolation Check(PlanKind kind, PlanDraftFacts facts)
    {
        if (facts.Name is not { Length: >= 1 and <= MaxNameLength } || facts.Name != facts.Name.Trim())
            return CatalogueRuleViolation.NameInvalid;
        if (!Amount(facts.MonthlyPricePaise) || !Amount(facts.AnnualPricePaise)
            || !Amount(facts.ExtraShopMonthlyPricePaise) || !Amount(facts.ExtraShopAnnualPricePaise))
            return CatalogueRuleViolation.AmountInvalid;
        if (!Capacity(facts.IncludedShops) || !Capacity(facts.MembersPerShop))
            return CatalogueRuleViolation.CapacityInvalid;
        if (!PolicyVersion(facts.CustomerTermsVersion) || !PolicyVersion(facts.RefundPolicyVersion))
            return CatalogueRuleViolation.PolicyVersionInvalid;
        return kind switch
        {
            PlanKind.SingleShop => CheckSingleShop(facts),
            PlanKind.MultiShop => CheckMultiShop(facts),
            _ => CheckCustom(facts),
        };
    }

    /// <summary>Facts implied by the kind: a single-shop plan covers one shop and offers no extra shops.</summary>
    public static PlanDraftFacts Normalize(PlanKind kind, PlanDraftFacts facts) => kind switch
    {
        PlanKind.SingleShop => facts with { IncludedShops = 1, ExtraShopsOffered = false },
        PlanKind.Custom => facts with { ExtraShopsOffered = false },
        _ => facts,
    };

    public static string[] Missing(PlanKind kind, PlanDraftFacts facts)
    {
        var missing = new List<string>();
        if (kind == PlanKind.Custom)
        {
            if (facts.MonthlyPricePaise is null && facts.AnnualPricePaise is null) missing.Add("termPrice");
        }
        else
        {
            if (facts.MonthlyPricePaise is null) missing.Add("monthlyPricePaise");
            if (facts.AnnualPricePaise is null) missing.Add("annualPricePaise");
        }
        if (facts.IncludedShops is null) missing.Add("includedShops");
        if (facts.MembersPerShop is null) missing.Add("membersPerShop");
        if (kind == PlanKind.MultiShop) AddExtraShopGaps(facts, missing);
        if (facts.CustomerTermsVersion is null) missing.Add("customerTermsVersion");
        if (facts.RefundPolicyVersion is null) missing.Add("refundPolicyVersion");
        return [.. missing];
    }

    public static CatalogueRuleViolation Check(TrialPolicyFacts facts)
    {
        if (!Capacity(facts.ActiveMemberCap) || !Capacity(facts.MaxConcurrentTrialShops))
            return CatalogueRuleViolation.CapacityInvalid;
        return PolicyVersion(facts.TrialTermsVersion) ? CatalogueRuleViolation.None
            : CatalogueRuleViolation.PolicyVersionInvalid;
    }

    public static string[] Missing(TrialPolicyFacts facts)
    {
        var missing = new List<string>();
        if (facts.ActiveMemberCap is null) missing.Add("activeMemberCap");
        if (facts.MaxConcurrentTrialShops is null) missing.Add("maxConcurrentTrialShops");
        if (facts.TrialTermsVersion is null) missing.Add("trialTermsVersion");
        return [.. missing];
    }

    private static CatalogueRuleViolation CheckSingleShop(PlanDraftFacts facts)
    {
        if (facts.NegotiationReason is not null) return CatalogueRuleViolation.ReasonNotAllowed;
        if (facts.IncludedShops is not (null or 1)) return CatalogueRuleViolation.CapacityInvalid;
        return NoExtraShopOffer(facts) ? CatalogueRuleViolation.None : CatalogueRuleViolation.ExtraShopTermsInvalid;
    }

    private static CatalogueRuleViolation CheckMultiShop(PlanDraftFacts facts)
    {
        if (facts.NegotiationReason is not null) return CatalogueRuleViolation.ReasonNotAllowed;
        if (facts.IncludedShops is < 2) return CatalogueRuleViolation.CapacityInvalid;
        var pricesSet = facts.ExtraShopMonthlyPricePaise is not null || facts.ExtraShopAnnualPricePaise is not null;
        return pricesSet && facts.ExtraShopsOffered is not true
            ? CatalogueRuleViolation.ExtraShopTermsInvalid : CatalogueRuleViolation.None;
    }

    private static CatalogueRuleViolation CheckCustom(PlanDraftFacts facts)
    {
        if (facts.NegotiationReason is not { } reason || reason.Trim().Length == 0)
            return CatalogueRuleViolation.CustomReasonRequired;
        if (reason.Length > MaxReasonLength || reason != reason.Trim()) return CatalogueRuleViolation.CustomReasonRequired;
        return NoExtraShopOffer(facts) ? CatalogueRuleViolation.None : CatalogueRuleViolation.ExtraShopTermsInvalid;
    }

    private static void AddExtraShopGaps(PlanDraftFacts facts, List<string> missing)
    {
        if (facts.ExtraShopsOffered is null) missing.Add("extraShopsOffered");
        if (facts.ExtraShopsOffered is not true) return;
        if (facts.ExtraShopMonthlyPricePaise is null) missing.Add("extraShopMonthlyPricePaise");
        if (facts.ExtraShopAnnualPricePaise is null) missing.Add("extraShopAnnualPricePaise");
    }

    private static bool NoExtraShopOffer(PlanDraftFacts facts) => facts.ExtraShopsOffered is not true
        && facts.ExtraShopMonthlyPricePaise is null && facts.ExtraShopAnnualPricePaise is null;

    private static bool Amount(long? paise) => paise is null or (>= 0 and <= MaxPaise);

    private static bool Capacity(int? count) => count is null or (>= MinCapacity and <= MaxCapacity);

    private static bool PolicyVersion(string? version) => version is null || PolicyVersionPattern().IsMatch(version);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,39}\\z", RegexOptions.CultureInvariant, 200)]
    private static partial Regex PolicyVersionPattern();
}
