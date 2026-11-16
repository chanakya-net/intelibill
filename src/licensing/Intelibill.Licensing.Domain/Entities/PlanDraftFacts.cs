namespace Intelibill.Licensing.Domain.Entities;

/// <summary>
/// The commercial facts of one plan version. A null fact is missing, never unlimited and never defaulted;
/// zero is an explicit fact. Amounts are whole INR paise, capacities whole counts.
/// </summary>
public sealed record PlanDraftFacts(
    string Name,
    long? MonthlyPricePaise,
    long? AnnualPricePaise,
    int? IncludedShops,
    int? MembersPerShop,
    bool? ExtraShopsOffered,
    long? ExtraShopMonthlyPricePaise,
    long? ExtraShopAnnualPricePaise,
    string? CustomerTermsVersion,
    string? RefundPolicyVersion,
    string? NegotiationReason);

/// <summary>Trial policy facts. A null cap is missing; there is no unlimited trial cap.</summary>
public sealed record TrialPolicyFacts(int? ActiveMemberCap, int? MaxConcurrentTrialShops, string? TrialTermsVersion);
