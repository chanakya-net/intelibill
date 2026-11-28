using System.Security.Cryptography;
using System.Text;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Notifications;

/// <summary>
/// Plans durable, role-appropriate licence notification intents across accounts and shops.
/// Reads business time once per pass, computes thresholds before live merchant HTTP lookups,
/// keeps HTTP outside SQL transactions, and delegates atomic lock-rechecked insertion to
/// <see cref="INotificationPlanStore"/>.
/// </summary>
public sealed partial class NotificationPlanner(
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
        var batch = await PlanBatchAsync(
            afterAccount: null,
            maxAccounts: int.MaxValue,
            accountPageSize: accountPageSize,
            termPageSize: termPageSize,
            memberPageSize: memberPageSize,
            ct: ct);
        return batch.Pass;
    }

    public async Task<NotificationBatchResult> PlanBatchAsync(
        Guid? afterAccount,
        int maxAccounts,
        int accountPageSize,
        int termPageSize,
        int memberPageSize,
        CancellationToken ct)
    {
        var now = SubscriptionStatusProjection.EvaluationInstant(statusClock);
        var totals = new PassTotals();
        var cursor = afterAccount;
        var limit = Math.Max(1, maxAccounts);
        Guid? nextCursor = null;

        while (totals.AccountsScanned < limit)
        {
            var pageTake = Math.Min(Math.Clamp(accountPageSize, 1, 100), limit - totals.AccountsScanned);
            var accounts = await store.DiscoverAccountsAsync(now, cursor, pageTake, ct);
            if (accounts.Count == 0)
            {
                nextCursor = null;
                break;
            }

            foreach (var accountId in accounts)
            {
                totals.AccountsScanned++;
                var outcome = await PlanAccountAtAsync(accountId, now, termPageSize, memberPageSize, ct);
                totals.Record(outcome);
            }

            cursor = accounts[^1];
            nextCursor = accounts.Count < pageTake ? null : cursor;
            if (accounts.Count < pageTake)
                break;
        }

        return new NotificationBatchResult(totals.ToResult(), nextCursor);
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

        var evaluatedTerms = EvaluateTerms(context, now);
        if (evaluatedTerms.Count == 0)
            return new NotificationPlanCommitResult(0, 0, 0);

        var needsActors = evaluatedTerms.Any(term => term.CommercialThresholds.Count > 0)
            || evaluatedTerms.Any(term => term.ShopPlans.Count > 0);
        var verifiedActors = needsActors
            ? await ResolveAccountActorsAsync(accountId, CollectAccountActorIds(context), ct)
            : [];
        if (verifiedActors is null)
            return null;

        var commercialRecipients = NotificationRecipientPlanner.BuildCommercialRecipients(context, verifiedActors);
        var commercialUserIds = commercialRecipients
            .Where(recipient => recipient.MerchantUserId is not null)
            .Select(recipient => recipient.MerchantUserId!.Value)
            .ToHashSet();

        var writes = new List<PlannedNotificationWrite>();
        var shopMembersCache = new Dictionary<(Guid ShopId, Guid AffiliationId), IReadOnlyList<NoticeRecipientActorV1>?>();
        foreach (var evaluated in evaluatedTerms)
        {
            var collected = await CollectEvaluatedTermWritesAsync(
                context,
                evaluated,
                memberPageSize,
                commercialRecipients,
                commercialUserIds,
                shopMembersCache,
                writes,
                ct);
            if (!collected)
                return null;
        }

        return writes.Count == 0
            ? new NotificationPlanCommitResult(0, 0, 0)
            : await store.CommitPlannedJobsAsync(accountId, now, writes, ct);
    }

    private async Task<IReadOnlyList<NoticeRecipientActorV1>?> GetOrResolveShopMembersAsync(
        Guid accountId,
        ShopPlanSource shop,
        int memberPageSize,
        Dictionary<(Guid ShopId, Guid AffiliationId), IReadOnlyList<NoticeRecipientActorV1>?> cache,
        CancellationToken ct)
    {
        var key = (shop.ShopId, shop.AffiliationRequestId);
        if (cache.TryGetValue(key, out var existing))
            return existing;

        var resolved = await ResolveShopMembersAsync(
            accountId,
            shop.ShopId,
            shop.AffiliationRequestId,
            memberPageSize,
            ct);
        cache[key] = resolved;
        return resolved;
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
        $"email:{Convert.ToHexStringLower(HashEmail(normalizedEmail))}";

    public static byte[] HashEmail(string normalizedEmail) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeEmail(normalizedEmail)));

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>
    /// Produces a bounded masked destination hint that never equals the full email address for any
    /// valid local-part length, including single-character local parts.
    /// </summary>
    public static string MaskEmail(string normalizedEmail)
    {
        var at = normalizedEmail.IndexOf('@');
        if (at <= 0 || at >= normalizedEmail.Length - 1)
            return "***";

        var local = normalizedEmail[..at];
        var domain = normalizedEmail[(at + 1)..];
        var boundedDomain = domain.Length > 64 ? "…" + domain[^63..] : domain;
        var prefix = local.Length <= 1 ? "***" : $"{local[..1]}***";
        var hint = $"{prefix}@{boundedDomain}";
        return string.Equals(hint, normalizedEmail, StringComparison.OrdinalIgnoreCase)
            ? $"****@{boundedDomain}"
            : hint;
    }

    private sealed class PassTotals
    {
        public int AccountsScanned { get; set; }
        public int AccountsSkippedUnavailable { get; set; }
        public int Inserted { get; set; }
        public int SkippedDuplicate { get; set; }
        public int SkippedAuthorityChanged { get; set; }

        public void Record(NotificationPlanCommitResult? outcome)
        {
            if (outcome is null)
            {
                AccountsSkippedUnavailable++;
                return;
            }

            Inserted += outcome.Inserted;
            SkippedDuplicate += outcome.SkippedDuplicate;
            SkippedAuthorityChanged += outcome.SkippedAuthorityChanged;
        }

        public NotificationPassResult ToResult() =>
            new(AccountsScanned, AccountsSkippedUnavailable, Inserted, SkippedDuplicate, SkippedAuthorityChanged);
    }
}
