using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;

namespace Intelibill.Licensing.Application.Catalogue;

internal static class CatalogueDraftViews
{
    private static readonly (PlanKind Kind, string Name)[] Kinds =
    [
        (PlanKind.SingleShop, "single-shop"),
        (PlanKind.MultiShop, "multi-shop"),
        (PlanKind.Custom, "custom"),
    ];

    internal static PlanKind? ParseKind(string? name) =>
        Array.Find(Kinds, item => item.Name == name) is { Name: not null } match ? match.Kind : null;

    internal static string KindName(PlanKind kind) => Array.Find(Kinds, item => item.Kind == kind).Name;

    internal static PlanItem ToItem(Plan plan, PlanVersion draft)
    {
        var missing = CatalogueDraftRules.Missing(plan.Kind, draft.Facts);
        return new PlanItem(plan.Id, KindName(plan.Kind), new PlanDraftView(draft.Id, draft.VersionNumber, "draft",
            draft.Revision, draft.Name, draft.CurrencyCode, draft.MonthlyPricePaise, draft.AnnualPricePaise,
            draft.IncludedShops, draft.MembersPerShop, draft.ExtraShopsOffered, draft.ExtraShopMonthlyPricePaise,
            draft.ExtraShopAnnualPricePaise, draft.CustomerTermsVersion, draft.RefundPolicyVersion,
            draft.NegotiationReason, missing, missing.Length == 0, draft.UpdatedAt));
    }

    internal static TrialDraftView ToView(TrialPolicyVersion draft)
    {
        var missing = CatalogueDraftRules.Missing(draft.Facts);
        return new TrialDraftView(draft.Id, draft.VersionNumber, "draft", draft.Revision, draft.DurationDays,
            draft.ActiveMemberCap, draft.MaxConcurrentTrialShops, draft.TrialTermsVersion, missing,
            missing.Length == 0, draft.UpdatedAt);
    }
}
