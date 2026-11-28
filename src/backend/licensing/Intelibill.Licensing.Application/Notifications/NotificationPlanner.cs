using System.Security.Cryptography;
using System.Text;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Notifications;

/// <summary>
/// Plans durable, role-appropriate licence notification intents across accounts and shops.
/// Reads business time once per pass, keeps merchant HTTP verification outside SQL transactions,
/// and delegates atomic lock-rechecked insertion to <see cref="INotificationPlanStore"/>.
/// </summary>
public sealed class NotificationPlanner(
    INotificationPlanStore store,
    IMerchantNoticeRecipientAuthority merchantAuthority,
    TimeProvider statusClock,
    TimeProvider transportClock)
{
    private static readonly TimeSpan AuthorityRequestBudget = TimeSpan.FromSeconds(15);

    public async Task<NotificationPassResult> PlanAllDueAsync(
        CancellationToken ct,
        int accountPageSize = 20,
        int termPageSize = 50,
        int memberPageSize = 50)
    {
        var now = SubscriptionStatusProjection.EvaluationInstant(statusClock);
        var accountsScanned = 0;
        var accountsSkipped = 0;
        var inserted = 0;
        var duplicates = 0;
        var authorityChanged = 0;
        Guid? afterAccount = null;

        while (true)
        {
            var accounts = await store.DiscoverAccountsAsync(afterAccount, accountPageSize, ct);
            if (accounts.Count == 0)
                break;

            foreach (var accountId in accounts)
            {
                accountsScanned++;
                var outcome = await PlanAccountAtAsync(accountId, now, termPageSize, memberPageSize, ct);
                if (outcome is null)
                {
                    accountsSkipped++;
                    continue;
                }

                inserted += outcome.Inserted;
                duplicates += outcome.SkippedDuplicate;
                authorityChanged += outcome.SkippedAuthorityChanged;
            }

            afterAccount = accounts[^1];
            if (accounts.Count < accountPageSize)
                break;
        }

        return new NotificationPassResult(accountsScanned, accountsSkipped, inserted, duplicates, authorityChanged);
    }

    public Task<NotificationPlanCommitResult?> PlanAccountAsync(
        Guid accountId,
        CancellationToken ct,
        int termPageSize = 50,
        int memberPageSize = 50)
    {
        var now = SubscriptionStatusProjection.EvaluationInstant(statusClock);
        return PlanAccountAtAsync(accountId, now, termPageSize, memberPageSize, ct);
    }

    private async Task<NotificationPlanCommitResult?> PlanAccountAtAsync(
        Guid accountId,
        DateTimeOffset now,
        int termPageSize,
        int memberPageSize,
        CancellationToken ct)
    {
        var context = await store.ReadAccountPlanContextAsync(accountId, now, termPageSize, ct);
        if (context is null || context.IsAccountClosed || context.Terms.Count == 0)
            return new NotificationPlanCommitResult(0, 0, 0);

        var actorLookupIds = CollectAccountActorIds(context);
        var verifiedActors = await ResolveAccountActorsAsync(accountId, actorLookupIds, ct);
        if (verifiedActors is null)
            return null;

        var commercialRecipients = NotificationRecipientPlanner.BuildCommercialRecipients(context, verifiedActors);
        var commercialUserIds = commercialRecipients
            .Where(recipient => recipient.MerchantUserId is not null)
            .Select(recipient => recipient.MerchantUserId!.Value)
            .ToHashSet();

        var writes = new List<PlannedNotificationWrite>();
        var shopMembersCache = new Dictionary<(Guid ShopId, Guid AffiliationId), IReadOnlyList<NoticeRecipientActorV1>?>();
        foreach (var termSource in context.Terms)
        {
            var collected = await CollectTermWritesAsync(
                context,
                termSource,
                now,
                memberPageSize,
                commercialRecipients,
                commercialUserIds,
                shopMembersCache,
                writes,
                ct);
            if (!collected)
                return null;
        }

        if (writes.Count == 0)
            return new NotificationPlanCommitResult(0, 0, 0);

        return await store.CommitPlannedJobsAsync(accountId, now, writes, ct);
    }

    private async Task<bool> CollectTermWritesAsync(
        AccountPlanContext context,
        TermPlanSource termSource,
        DateTimeOffset now,
        int memberPageSize,
        List<ResolvedCommercialRecipient> commercialRecipients,
        HashSet<Guid> commercialUserIds,
        Dictionary<(Guid ShopId, Guid AffiliationId), IReadOnlyList<NoticeRecipientActorV1>?> shopMembersCache,
        List<PlannedNotificationWrite> writes,
        CancellationToken ct)
    {
        var facts = SubscriptionStatusProjection.ToFacts(termSource.Term);
        if (facts.Evidence != TermEvidenceState.Complete || facts.Basis is null)
            return true;

        var activeShops = termSource.Shops.Where(shop => !shop.IsClosed).ToArray();
        var allShopsClosed = termSource.Shops.Count > 0 && activeShops.Length == 0;
        var termSuspended = context.IsAccountSuspended
            || (activeShops.Length > 0 && activeShops.All(shop => shop.IsSuspended));

        var commercialThresholds = NotificationSchedulePolicy.PlanThresholds(
            facts,
            now,
            isSuspended: termSuspended,
            isClosed: context.IsAccountClosed || allShopsClosed);

        if (commercialThresholds.Count > 0 && commercialRecipients.Count > 0)
        {
            NotificationRecipientPlanner.AddCommercialWrites(
                context,
                termSource,
                facts,
                activeShops,
                commercialThresholds,
                commercialRecipients,
                writes);
        }

        foreach (var shop in activeShops)
        {
            var shopFacts = shop.HandoffEndsAt is { } handoff && facts.Basis == SubscriptionBasis.Paid
                ? facts with { HandoffEndsAt = handoff }
                : facts;
            var shopSuspended = context.IsAccountSuspended || shop.IsSuspended;
            var shopThresholds = NotificationSchedulePolicy.PlanThresholds(
                shopFacts,
                now,
                isSuspended: shopSuspended,
                isClosed: false);
            if (shopThresholds.Count == 0)
                continue;

            var key = (shop.ShopId, shop.AffiliationRequestId);
            if (!shopMembersCache.TryGetValue(key, out var shopMembers))
            {
                shopMembers = await ResolveShopMembersAsync(
                    context.AccountId,
                    shop.ShopId,
                    shop.AffiliationRequestId,
                    memberPageSize,
                    ct);
                shopMembersCache[key] = shopMembers;
            }

            if (shopMembers is null)
                return false;
            if (shopMembers.Count == 0)
                continue;

            NotificationRecipientPlanner.AddOperationalShopWrites(
                context.AccountId,
                termSource,
                shopFacts,
                shop,
                shopThresholds,
                shopMembers,
                commercialUserIds,
                writes);
        }

        return true;
    }

    private static Guid[] CollectAccountActorIds(AccountPlanContext context) =>
    [
        .. context.ActiveMembers.Select(member => member.MerchantUserId)
            .Concat(context.EligibleContacts
                .Where(contact => contact.MerchantUserId is not null)
                .Select(contact => contact.MerchantUserId!.Value))
            .Distinct()
            .OrderBy(id => id),
    ];

    private async Task<Dictionary<Guid, NoticeRecipientActorV1>?> ResolveAccountActorsAsync(
        Guid accountId,
        Guid[] actorIds,
        CancellationToken ct)
    {
        var resolved = new Dictionary<Guid, NoticeRecipientActorV1>();
        if (actorIds.Length == 0)
            return resolved;

        foreach (var batch in actorIds.Chunk(LicenseNoticesV1.MaxActorLookupCount))
        {
            var request = new NoticeRecipientAuthorityRequestV1(
                LicenseNoticesV1.SchemaVersion,
                Guid.NewGuid(),
                transportClock.GetUtcNow().Add(AuthorityRequestBudget),
                accountId,
                AffiliationRequestId: null,
                ShopId: null,
                batch,
                AfterUserId: null,
                PageSize: LicenseNoticesV1.MaxRecipientPageSize);
            var response = await merchantAuthority.InspectAsync(request, ct);
            if (response is null)
                return null;

            foreach (var actor in response.Actors)
                resolved[actor.UserId] = actor;
        }

        return resolved;
    }

    private async Task<IReadOnlyList<NoticeRecipientActorV1>?> ResolveShopMembersAsync(
        Guid accountId,
        Guid shopId,
        Guid affiliationRequestId,
        int memberPageSize,
        CancellationToken ct)
    {
        var clampedPageSize = Math.Clamp(memberPageSize, 1, LicenseNoticesV1.MaxRecipientPageSize);
        var members = new List<NoticeRecipientActorV1>();
        var seen = new HashSet<Guid>();
        Guid? afterUser = null;

        while (true)
        {
            var request = new NoticeRecipientAuthorityRequestV1(
                LicenseNoticesV1.SchemaVersion,
                Guid.NewGuid(),
                transportClock.GetUtcNow().Add(AuthorityRequestBudget),
                accountId,
                affiliationRequestId,
                shopId,
                ActorIds: [],
                AfterUserId: afterUser,
                PageSize: clampedPageSize);
            var response = await merchantAuthority.InspectAsync(request, ct);
            if (response is null)
                return null;
            if (!response.AffiliationMatches)
                return [];

            foreach (var member in response.ShopMembers)
            {
                if (seen.Add(member.UserId))
                    members.Add(member);
            }

            if (response.NextShopMemberCursor is not { } next)
                break;
            if (afterUser is { } previous && next.CompareTo(previous) <= 0)
                return null;
            afterUser = next;
        }

        return members;
    }

    public static string UserRecipientKey(Guid userId) => $"user:{userId:D}";

    public static string EmailRecipientKey(string normalizedEmail) =>
        $"email:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeEmail(normalizedEmail))))}";

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static string MaskEmail(string normalizedEmail)
    {
        var at = normalizedEmail.IndexOf('@');
        if (at <= 0 || at >= normalizedEmail.Length - 1)
            return "***";
        var local = normalizedEmail[..at];
        var domain = normalizedEmail[(at + 1)..];
        var prefix = local.Length == 1 ? local[..1] : local[..1] + "***";
        return $"{prefix}@{domain}";
    }
}
