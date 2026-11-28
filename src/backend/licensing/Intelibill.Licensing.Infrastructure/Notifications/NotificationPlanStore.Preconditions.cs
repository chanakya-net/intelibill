using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed partial class NotificationPlanStore
{
    private async Task<LiveCommitSnapshot> LoadLiveCommitPreconditionsAsync(
        Guid accountId,
        Guid[] sourceIds,
        DateTimeOffset decisionAt,
        DateTimeOffset plannedAt,
        CancellationToken ct)
    {
        var isAccountSuspended = await NotificationPlanSourceQueries.IsAccountSuspendedAsync(db, accountId, decisionAt, ct);
        var terms = await NotificationPlanSourceQueries.ReadEffectiveTermSourcesAsync(
            db,
            factsReader,
            accountId,
            decisionAt,
            termPageSize: 100,
            filterTermIds: sourceIds,
            ct);
        var commercial = new Dictionary<Guid, LiveCommercialCheck>();
        var operational = new Dictionary<(Guid TermId, Guid ShopId, Guid AffiliationId), LiveShopCheck>();

        foreach (var termSource in terms)
            CollectLiveTermCheck(isAccountSuspended, termSource, plannedAt, commercial, operational);

        return new LiveCommitSnapshot(commercial, operational);
    }

    private static void CollectLiveTermCheck(
        bool isAccountSuspended,
        TermPlanSource termSource,
        DateTimeOffset plannedAt,
        Dictionary<Guid, LiveCommercialCheck> commercial,
        Dictionary<(Guid TermId, Guid ShopId, Guid AffiliationId), LiveShopCheck> operational)
    {
        var facts = SubscriptionStatusProjection.ToFacts(termSource.Term);
        if (facts.Evidence != TermEvidenceState.Complete || facts.Basis is null)
            return;

        var sourceKind = facts.Basis.Value.ToString();
        var cancelVersion = SubscriptionStatusProjection.ActiveCancellation(termSource.Term)?.Version ?? 0L;
        var activeShops = termSource.Shops.Where(shop => !shop.IsClosed).ToArray();
        var allShopsClosed = termSource.Shops.Count > 0 && activeShops.Length == 0;
        var restriction = NotificationPlanner.ResolveRestrictionScope(isAccountSuspended, activeShops, out _);
        var commercialThresholds = NotificationSchedulePolicy.PlanThresholds(facts, plannedAt, restriction, allShopsClosed);
        if (commercialThresholds.Count > 0)
        {
            var commRev = NotificationSchedulePolicy.BuildCommercialSourceRevision(
                sourceKind,
                termSource.Term.TermId,
                facts,
                termSource.TrialExtensionRevision,
                cancelVersion,
                termSource.Term.PricePaise,
                termSource.Term.Currency);
            commercial[termSource.Term.TermId] = new LiveCommercialCheck(
                commRev,
                restriction,
                NotificationPlanner.BuildRosterFingerprint(isAccountSuspended, activeShops),
                ToThresholdSet(commercialThresholds));
        }

        CollectLiveShopChecks(
            isAccountSuspended,
            termSource,
            facts,
            sourceKind,
            cancelVersion,
            activeShops,
            plannedAt,
            operational);
    }

    private static void CollectLiveShopChecks(
        bool isAccountSuspended,
        TermPlanSource termSource,
        SubscriptionAccessFacts facts,
        string sourceKind,
        long cancelVersion,
        ShopPlanSource[] activeShops,
        DateTimeOffset plannedAt,
        Dictionary<(Guid TermId, Guid ShopId, Guid AffiliationId), LiveShopCheck> operational)
    {
        foreach (var shop in activeShops)
        {
            var shopFacts = NotificationSchedulePolicy.OperationalFacts(
                facts, facts.StartsAt, shop.CoverageFrom, shop.HandoffEndsAt);
            var shopRestriction = isAccountSuspended || shop.IsSuspended
                ? NoticeRestrictionScope.Full
                : NoticeRestrictionScope.None;
            var shopThresholds = NotificationSchedulePolicy.PlanThresholds(shopFacts, plannedAt, shopRestriction, false);
            if (shopThresholds.Count == 0)
                continue;

            var opRev = NotificationSchedulePolicy.BuildOperationalSourceRevision(
                sourceKind,
                termSource.Term.TermId,
                shopFacts,
                termSource.TrialExtensionRevision,
                cancelVersion);
            operational[(termSource.Term.TermId, shop.ShopId, shop.AffiliationRequestId)] = new LiveShopCheck(
                opRev,
                shopRestriction,
                ToThresholdSet(shopThresholds));
        }
    }

    private async Task<ExistingJobSets> LoadExistingKeysAsync(
        Guid accountId,
        Guid[] sourceIds,
        CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<ExistingJobRow>($"""
            SELECT id, scope_kind, scope_id, recipient_key, recipient_user_id, purpose, source_kind, source_id,
                source_revision, threshold, destination_hash, allowed_channels
            FROM public.effective_notification_jobs
            WHERE account_id = {accountId} AND source_id = ANY({sourceIds})
            """).ToListAsync(ct);
        return new ExistingJobSets(rows);
    }

    private async Task<Dictionary<Guid, int>> LockActiveMembersAsync(
        Guid accountId,
        IReadOnlyList<PlannedNotificationWrite> writes,
        CancellationToken ct)
    {
        var userIds = writes
            .Where(write => write.Audience == LicenseNoticesV1.AudienceCommercialBillingRole && write.RecipientUserId is not null)
            .Select(write => write.RecipientUserId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var active = new Dictionary<Guid, int>();
        foreach (var userId in userIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.billing_actor_id', {userId.ToString("D")}, true)",
                ct);
            var rows = await db.Database.SqlQuery<MemberLockRow>($"""
                SELECT merchant_user_id, revision
                FROM public.billing_account_members
                WHERE account_id = {accountId}
                  AND merchant_user_id = {userId}
                  AND status = {(int)BillingAccountMemberStatus.Active}
                  AND role IN ({(int)BillingAccountRole.Owner}, {(int)BillingAccountRole.Manager})
                FOR SHARE
                """).ToListAsync(ct);
            if (rows is [var found])
                active[found.MerchantUserId] = found.Revision;
        }

        if (userIds.Length > 0)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.billing_actor_id', {string.Empty}, true)",
                ct);
        }

        return active;
    }

    private async Task<Dictionary<Guid, int>> LockEligibleContactsAsync(
        Guid accountId,
        IReadOnlyList<PlannedNotificationWrite> writes,
        CancellationToken ct)
    {
        var contactIds = writes
            .Where(write => write.Audience == LicenseNoticesV1.AudienceCommercialContact && write.ContactId is not null)
            .Select(write => write.ContactId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var eligible = new Dictionary<Guid, int>();
        foreach (var contactId in contactIds)
        {
            var rows = await db.Database.SqlQuery<ContactLockRow>($"""
                SELECT id, revision
                FROM public.billing_notice_contacts
                WHERE account_id = {accountId}
                  AND id = {contactId}
                  AND verified_at IS NOT NULL
                  AND opted_in_at IS NOT NULL
                  AND revoked_at IS NULL
                FOR SHARE
                """).ToListAsync(ct);
            if (rows is [var found])
                eligible[found.Id] = found.Revision;
        }

        return eligible;
    }

    private static bool IsStillValid(
        PlannedNotificationWrite write,
        Dictionary<Guid, int> validMembers,
        Dictionary<Guid, int> validContacts,
        LiveCommitSnapshot live)
    {
        var expectedThreshold = new ThresholdTuple(write.Purpose, write.Threshold, write.DueAt, write.EffectiveDeadlineAt);
        if (write.ScopeKind == LicenseNoticesV1.ScopeAccount)
        {
            if (!IsCommercialRecipientStillValid(write, validMembers, validContacts))
                return false;
            return live.Commercial.TryGetValue(write.SourceId, out var check)
                && string.Equals(check.SourceRevision, write.SourceRevision, StringComparison.Ordinal)
                && check.Restriction == write.ExpectedRestriction
                && string.Equals(check.RosterFingerprint, write.ExpectedRosterFingerprint, StringComparison.Ordinal)
                && check.Thresholds.Contains(expectedThreshold);
        }

        return write.ExpectedAffiliationRequestId is { } affiliationId
            && live.Operational.TryGetValue((write.SourceId, write.ScopeId, affiliationId), out var shopCheck)
            && string.Equals(shopCheck.SourceRevision, write.SourceRevision, StringComparison.Ordinal)
            && shopCheck.Restriction == write.ExpectedRestriction
            && shopCheck.Thresholds.Contains(expectedThreshold);
    }

    private static bool IsCommercialRecipientStillValid(
        PlannedNotificationWrite write,
        Dictionary<Guid, int> validMembers,
        Dictionary<Guid, int> validContacts)
    {
        if (write.Audience == LicenseNoticesV1.AudienceCommercialBillingRole)
        {
            return write.RecipientUserId is { } userId
                && validMembers.TryGetValue(userId, out var revision)
                && revision == write.ExpectedMemberRevision;
        }

        return write.ContactId is { } contactId
            && validContacts.TryGetValue(contactId, out var contactRev)
            && contactRev == write.ExpectedContactRevision;
    }

    private static HashSet<ThresholdTuple> ToThresholdSet(IReadOnlyList<PlannedThreshold> thresholds) =>
        thresholds.Select(item => new ThresholdTuple(item.Purpose, item.Threshold, item.DueAt, item.EffectiveDeadlineAt)).ToHashSet();

    private sealed record MemberLockRow(Guid MerchantUserId, int Revision);
    private sealed record ContactLockRow(Guid Id, int Revision);

    private sealed record ExistingJobRow(
        Guid Id,
        string ScopeKind,
        Guid ScopeId,
        string RecipientKey,
        Guid? RecipientUserId,
        string Purpose,
        string SourceKind,
        Guid SourceId,
        string SourceRevision,
        string Threshold,
        byte[]? DestinationHash,
        string[] AllowedChannels);

    private sealed record ThresholdTuple(
        string Purpose,
        string Threshold,
        DateTimeOffset DueAt,
        DateTimeOffset EffectiveDeadlineAt);

    private sealed record LiveCommercialCheck(
        string SourceRevision,
        NoticeRestrictionScope Restriction,
        string RosterFingerprint,
        HashSet<ThresholdTuple> Thresholds);

    private sealed record LiveShopCheck(
        string SourceRevision,
        NoticeRestrictionScope Restriction,
        HashSet<ThresholdTuple> Thresholds);

    private sealed record LiveCommitSnapshot(
        Dictionary<Guid, LiveCommercialCheck> Commercial,
        Dictionary<(Guid TermId, Guid ShopId, Guid AffiliationId), LiveShopCheck> Operational);
}
