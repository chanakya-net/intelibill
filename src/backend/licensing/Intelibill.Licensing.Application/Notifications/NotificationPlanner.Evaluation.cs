using System.Security.Cryptography;
using System.Text;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Notifications;

public sealed partial class NotificationPlanner
{
    private static List<EvaluatedTermPlan> EvaluateTerms(AccountPlanContext context, DateTimeOffset now)
    {
        var list = new List<EvaluatedTermPlan>();
        foreach (var termSource in context.Terms)
        {
            var facts = SubscriptionStatusProjection.ToFacts(termSource.Term);
            if (facts.Evidence != TermEvidenceState.Complete || facts.Basis is null)
                continue;

            var activeShops = termSource.Shops.Where(shop => !shop.IsClosed).ToArray();
            var allShopsClosed = termSource.Shops.Count > 0 && activeShops.Length == 0;
            var restriction = ResolveRestrictionScope(context.IsAccountSuspended, activeShops, out var suspendedCount);
            var commercialThresholds = NotificationSchedulePolicy.PlanThresholds(
                facts,
                now,
                restriction,
                isClosed: context.IsAccountClosed || allShopsClosed);
            var shopPlans = EvaluateShopPlans(context.IsAccountSuspended, facts, activeShops, now);

            if (commercialThresholds.Count > 0 || shopPlans.Count > 0)
            {
                list.Add(new EvaluatedTermPlan(
                    termSource,
                    facts,
                    activeShops,
                    restriction,
                    suspendedCount,
                    commercialThresholds,
                    shopPlans));
            }
        }

        return list;
    }

    private static List<EvaluatedShopPlan> EvaluateShopPlans(
        bool isAccountSuspended,
        SubscriptionAccessFacts facts,
        ShopPlanSource[] activeShops,
        DateTimeOffset now)
    {
        var plans = new List<EvaluatedShopPlan>();
        foreach (var shop in activeShops)
        {
            var shopFacts = NotificationSchedulePolicy.OperationalFacts(
                facts, facts.StartsAt, shop.CoverageFrom, shop.HandoffEndsAt);
            var restriction = isAccountSuspended || shop.IsSuspended
                ? NoticeRestrictionScope.Full
                : NoticeRestrictionScope.None;
            var thresholds = NotificationSchedulePolicy.PlanThresholds(shopFacts, now, restriction, isClosed: false);
            if (thresholds.Count > 0)
                plans.Add(new EvaluatedShopPlan(shop, shopFacts, restriction, thresholds));
        }

        return plans;
    }

    private async Task<bool> CollectEvaluatedTermWritesAsync(
        AccountPlanContext context,
        EvaluatedTermPlan evaluated,
        int memberPageSize,
        List<ResolvedCommercialRecipient> commercialRecipients,
        HashSet<Guid> commercialUserIds,
        Dictionary<(Guid ShopId, Guid AffiliationId), IReadOnlyList<NoticeRecipientActorV1>?> shopMembersCache,
        List<PlannedNotificationWrite> writes,
        CancellationToken ct)
    {
        if (evaluated.CommercialThresholds.Count > 0 && commercialRecipients.Count > 0)
        {
            var rosterFingerprint = BuildRosterFingerprint(context.IsAccountSuspended, evaluated.ActiveShops);
            NotificationRecipientPlanner.AddCommercialWrites(
                context,
                evaluated.Source,
                evaluated.Facts,
                evaluated.ActiveShops,
                evaluated.CommercialRestriction,
                evaluated.SuspendedShopCount,
                rosterFingerprint,
                evaluated.CommercialThresholds,
                commercialRecipients,
                writes);
        }

        foreach (var shopPlan in evaluated.ShopPlans)
        {
            var members = await GetOrResolveShopMembersAsync(
                context.AccountId,
                shopPlan.Shop,
                memberPageSize,
                shopMembersCache,
                ct);
            if (members is null)
                return false;
            if (members.Count == 0)
                continue;

            NotificationRecipientPlanner.AddOperationalShopWrites(
                context.AccountId,
                evaluated.Source,
                shopPlan.Facts,
                shopPlan.Shop,
                shopPlan.Restriction,
                shopPlan.Thresholds,
                members,
                commercialUserIds,
                writes);
        }

        return true;
    }

    public static NoticeRestrictionScope ResolveRestrictionScope(
        bool isAccountSuspended,
        IReadOnlyList<ShopPlanSource> activeShops,
        out int suspendedShopCount)
    {
        suspendedShopCount = activeShops.Count(shop => isAccountSuspended || shop.IsSuspended);
        if (isAccountSuspended || (activeShops.Count > 0 && suspendedShopCount == activeShops.Count))
            return NoticeRestrictionScope.Full;
        return suspendedShopCount > 0 ? NoticeRestrictionScope.Partial : NoticeRestrictionScope.None;
    }

    public static string BuildRosterFingerprint(bool isAccountSuspended, IReadOnlyList<ShopPlanSource> activeShops)
    {
        var entries = activeShops
            .OrderBy(shop => shop.ShopId)
            .Select(shop => $"{shop.ShopId:D}:{shop.AffiliationRequestId:D}:{(shop.IsSuspended ? 'S' : 'A')}");
        var raw = $"{(isAccountSuspended ? 'S' : 'A')}|{string.Join(',', entries)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private sealed record EvaluatedShopPlan(
        ShopPlanSource Shop,
        SubscriptionAccessFacts Facts,
        NoticeRestrictionScope Restriction,
        IReadOnlyList<PlannedThreshold> Thresholds);

    private sealed record EvaluatedTermPlan(
        TermPlanSource Source,
        SubscriptionAccessFacts Facts,
        ShopPlanSource[] ActiveShops,
        NoticeRestrictionScope CommercialRestriction,
        int SuspendedShopCount,
        IReadOnlyList<PlannedThreshold> CommercialThresholds,
        List<EvaluatedShopPlan> ShopPlans);
}
