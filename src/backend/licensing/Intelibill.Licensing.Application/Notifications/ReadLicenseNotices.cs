using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Notifications;

/// <summary>
/// Separate merchant shop, merchant billing-account, and provider preview reads over persisted
/// <see cref="NotificationJob"/> rows. Merchant reads project only the current applicable status
/// per live source; provider preview preserves full immutable schedule history. Strictly read-only.
/// </summary>
public sealed class ReadLicenseNoticesHandler(
    IBillingDelegationAccess access,
    ISubscriptionStatusStore statusStore,
    ISubscriptionAccessFactsReader factsReader,
    ISuspensionStore suspensions,
    IClosureStore closures,
    ILicenseNoticeReader reader,
    TimeProvider statusClock,
    TimeProvider sessionClock)
{
    private const AdminCapability Readers =
        AdminCapability.Support | AdminCapability.Billing | AdminCapability.LicensingManager;

    private const AdminCapability CommercialReaders =
        AdminCapability.Billing | AdminCapability.LicensingManager;

    public Task<ErrorOr<ShopLicenseNoticesV1>> ReadShopAsync(
        VerifiedBillingDelegation delegation,
        Guid shopId,
        Guid? after,
        CancellationToken ct)
    {
        if (delegation.AccountId is not null
            || delegation.Action != LicenseNoticesV1.ShopRead
            || shopId == Guid.Empty
            || after == Guid.Empty)
        {
            return Task.FromResult<ErrorOr<ShopLicenseNoticesV1>>(BillingDelegationErrors.Invalid);
        }

        return access.ExecuteAsync<ShopLicenseNoticesV1>(
            delegation,
            (scope, token) => ReadCurrentShopNoticesAsync(scope, shopId, after, token),
            ct);
    }

    public Task<ErrorOr<AccountLicenseNoticesV1>> ReadAccountAsync(
        VerifiedBillingDelegation delegation,
        Guid accountId,
        Guid? after,
        CancellationToken ct)
    {
        if (delegation.AccountId != accountId
            || delegation.Action != LicenseNoticesV1.AccountRead
            || accountId == Guid.Empty
            || after == Guid.Empty)
        {
            return Task.FromResult<ErrorOr<AccountLicenseNoticesV1>>(BillingDelegationErrors.Invalid);
        }

        // Delegation transaction already locks active Owner/Manager membership FOR SHARE.
        return access.ExecuteAsync<AccountLicenseNoticesV1>(
            delegation,
            (scope, token) => ReadCurrentAccountNoticesAsync(accountId, scope.ActorId, after, token),
            ct);
    }

    public Task<ErrorOr<ProviderNotificationsPageV1>> ReadProviderPreviewAsync(
        AdminSessionCaller caller,
        Guid accountId,
        Guid? after,
        CancellationToken ct) =>
        statusStore.InProviderTransactionAsync<ProviderNotificationsPageV1>(async token =>
        {
            if (accountId == Guid.Empty || after == Guid.Empty)
                return NotificationErrors.InvalidCursor;

            var grant = await AuthorizeProviderAsync(caller, token);
            if (grant.IsError)
                return grant.Errors;

            await statusStore.UseAccountAsync(accountId, token);
            var now = SubscriptionStatusProjection.EvaluationInstant(statusClock);
            var page = await reader.ReadProviderAccountJobsAsync(accountId, after, LicenseNoticesV1.PageSize, token);
            if (page.IsError)
                return page.Errors;

            var commercial = grant.Value;
            var items = page.Value.Rows.Select(row => ProjectProviderItem(row, now, commercial)).ToArray();
            var nextCursor = page.Value.HasMore && items.Length > 0 ? items[^1].JobId : (Guid?)null;
            return new ProviderNotificationsPageV1(
                LicenseNoticesV1.SchemaVersion,
                accountId,
                now,
                commercial,
                items,
                nextCursor);
        }, ct);

    private async Task<ErrorOr<ShopLicenseNoticesV1>> ReadCurrentShopNoticesAsync(
        IBillingDelegationScope scope,
        Guid shopId,
        Guid? after,
        CancellationToken ct)
    {
        var now = SubscriptionStatusProjection.EvaluationInstant(statusClock);
        await statusStore.UseShopAsync(shopId, ct);
        var coverage = await factsReader.ReadShopAsync(shopId, ct, now);
        if (coverage is null)
            return EmptyShopNotices(shopId, now);

        await scope.UseAccountAsync(coverage.AccountId, ct);
        var closure = await closures.ReadShopClosureStatusAsync(coverage.AccountId, shopId, ct);
        if (closure.ClosedAt is not null && closure.ReopenedAt is null)
            return EmptyShopNotices(shopId, now);

        var baseline = coverage.Term is { } baselineTerm ? SubscriptionStatusProjection.ToFacts(baselineTerm) : null;
        var grants = (coverage.Grants ?? []).Select(SubscriptionStatusProjection.ToFacts).ToArray();
        var chosen = ComplimentaryAccessPolicy.Select(baseline, grants, now);
        if (chosen is not -1 || coverage.Term is not { } currentTerm || baseline is null
            || baseline.Evidence != TermEvidenceState.Complete
            || baseline.Basis is null
            || baseline.RenewedAtEnd)
        {
            // Active complimentary grant, missing/incomplete term, or already-renewed predecessor has no due warning.
            return EmptyShopNotices(shopId, now);
        }

        if (after is not null)
            return EmptyShopNotices(shopId, now);

        var suspension = await suspensions.ReadCustomerAsync(coverage.AccountId, shopId, now, ct);
        var restriction = suspension is not null && suspension.EffectiveAt <= now
            ? NoticeRestrictionScope.Full
            : NoticeRestrictionScope.None;
        var sourceRevision = await BuildCurrentShopSourceRevisionAsync(coverage.AccountId, currentTerm, baseline, ct);
        var page = await reader.ReadCurrentDueShopNoticeAsync(
            coverage.AccountId,
            shopId,
            scope.ActorId,
            currentTerm.TermId,
            sourceRevision,
            now,
            ct);
        var items = page.Rows.Select(row => ProjectShopNotice(row, restriction)).ToArray();
        return new ShopLicenseNoticesV1(
            LicenseNoticesV1.SchemaVersion,
            shopId,
            now,
            items,
            page.NextDueAt,
            NextCursor: null);
    }

    private async Task<string> BuildCurrentShopSourceRevisionAsync(
        Guid accountId,
        TermFactsRow currentTerm,
        SubscriptionAccessFacts baseline,
        CancellationToken ct)
    {
        var trialExtRev = currentTerm.Trial is not null
            ? await reader.ReadTrialExtensionRevisionAsync(accountId, currentTerm.TermId, ct)
            : 0;
        var cancelVersion = SubscriptionStatusProjection.ActiveCancellation(currentTerm)?.Version ?? 0L;
        return NotificationSchedulePolicy.BuildOperationalSourceRevision(
            baseline.Basis!.Value.ToString(),
            currentTerm.TermId,
            NotificationSchedulePolicy.OperationalFacts(baseline, currentTerm.StartsAt, currentTerm.PaidFrom, currentTerm.HandoffEndsAt),
            trialExtRev,
            cancelVersion);
    }

    private async Task<ErrorOr<AccountLicenseNoticesV1>> ReadCurrentAccountNoticesAsync(
        Guid accountId,
        Guid actorId,
        Guid? after,
        CancellationToken ct)
    {
        var now = SubscriptionStatusProjection.EvaluationInstant(statusClock);
        if (await closures.AccountClosedAsync(accountId, ct))
            return new AccountLicenseNoticesV1(LicenseNoticesV1.SchemaVersion, accountId, now, [], null, null);

        var activeSources = await reader.ReadCurrentAccountSourcesAsync(accountId, actorId, now, ct);
        if (activeSources.Count == 0)
            return new AccountLicenseNoticesV1(LicenseNoticesV1.SchemaVersion, accountId, now, [], null, null);

        var bySource = activeSources.ToDictionary(source => source.SourceId);
        var page = await reader.ReadCurrentDueAccountNoticesAsync(
            accountId,
            actorId,
            activeSources,
            now,
            after,
            LicenseNoticesV1.PageSize,
            ct);
        var items = page.Rows
            .Select(row => ProjectAccountNotice(row, bySource.GetValueOrDefault(row.SourceId)))
            .ToArray();
        var nextCursor = page.HasMore && items.Length > 0 ? items[^1].NoticeId : (Guid?)null;
        return new AccountLicenseNoticesV1(
            LicenseNoticesV1.SchemaVersion,
            accountId,
            now,
            items,
            page.NextDueAt,
            nextCursor);
    }

    private async Task<ErrorOr<bool>> AuthorizeProviderAsync(AdminSessionCaller caller, CancellationToken ct)
    {
        var (actor, session) = await statusStore.LockProviderAsync(caller, ct);
        var now = sessionClock.GetUtcNow();
        if (session is null
            || now >= caller.AccessExpiresAt
            || !session.IsLiveAt(now)
            || actor is not { DeactivatedAt: null, MfaEnrolledAt: not null })
        {
            return AdminRefreshErrors.SessionEnded;
        }

        if ((actor.Capabilities & Readers) == 0)
            return SubscriptionStatusErrors.ProviderDenied;

        return (actor.Capabilities & CommercialReaders) != 0;
    }

    private static ShopLicenseNoticesV1 EmptyShopNotices(Guid shopId, DateTimeOffset now) =>
        new(LicenseNoticesV1.SchemaVersion, shopId, now, [], null, null);

    private static ShopLicenseNoticeItemV1 ProjectShopNotice(
        NotificationJobReadRow row,
        NoticeRestrictionScope liveRestriction)
    {
        using var args = JsonDocument.Parse(row.TemplateArguments);
        var root = args.RootElement;
        var baseState = ReadString(root, "accessState", "Active");
        var baseGuidance = ReadString(root, "guidanceCode", LicenseNoticesV1.GuidanceTrialEnding);
        var (state, guidance, _) = NotificationSchedulePolicy.ProjectLiveRestriction(
            row.Threshold,
            baseState,
            baseGuidance,
            liveRestriction);
        return new ShopLicenseNoticeItemV1(
            row.Id,
            row.ScopeId,
            row.Purpose,
            row.Threshold,
            state,
            guidance,
            NotificationSchedulePolicy.CurrentTemplateKey(row.TemplateKey, guidance),
            row.DueAt,
            row.EffectiveDeadlineAt,
            row.TimeZone,
            row.Locale,
            LicenseNoticesV1.BoundShopName(ReadString(root, "shopName", string.Empty)));
    }

    private static AccountLicenseNoticeItemV1 ProjectAccountNotice(
        NotificationJobReadRow row,
        CurrentCommercialSource? liveSource)
    {
        using var args = JsonDocument.Parse(row.TemplateArguments);
        var root = args.RootElement;
        var baseState = ReadString(root, "accessState", "Active");
        var baseGuidance = ReadString(root, "guidanceCode", LicenseNoticesV1.GuidancePaidRenewalDue);
        var (state, guidance, noLift) = liveSource is not null
            ? NotificationSchedulePolicy.ProjectLiveRestriction(row.Threshold, baseState, baseGuidance, liveSource.Restriction)
            : (baseState, baseGuidance, ReadBool(root, "paymentDoesNotLiftSuspension"));
        var coveredCount = liveSource?.CoveredShopCount ?? ReadInt(root, "coveredShopCount");
        var suspendedCount = liveSource?.SuspendedShopCount ?? ReadInt(root, "suspendedShopCount");
        var coveredNames = liveSource?.CoveredShopNames
            ?? LicenseNoticesV1.BoundShopNameSample(ReadStringArray(root, "coveredShopNames"));
        var truncated = liveSource?.CoveredShopNamesTruncated ?? (coveredCount > coveredNames.Length);
        return new AccountLicenseNoticeItemV1(
            row.Id,
            row.AccountId,
            row.Purpose,
            row.SourceKind,
            row.SourceId,
            row.Threshold,
            state,
            guidance,
            NotificationSchedulePolicy.CurrentTemplateKey(row.TemplateKey, guidance),
            row.DueAt,
            row.EffectiveDeadlineAt,
            row.TimeZone,
            row.Locale,
            row.AllowedChannels,
            coveredCount,
            coveredNames,
            ReadNullableLong(root, "evidencedTermPricePaise"),
            ReadNullableString(root, "evidencedTermCurrency"),
            ReadBool(root, "renewalQuoteRequired"),
            noLift,
            suspendedCount,
            truncated);
    }

    private static ProviderNotificationItemV1 ProjectProviderItem(
        NotificationJobReadRow row,
        DateTimeOffset now,
        bool commercialAllowed)
    {
        using var args = JsonDocument.Parse(row.TemplateArguments);
        var root = args.RootElement;
        var isCommercialAudience = row.ScopeKind == LicenseNoticesV1.ScopeAccount;
        var coveredShopCount = isCommercialAudience ? ReadInt(root, "coveredShopCount") : 1;
        var suspendedShopCount = isCommercialAudience ? ReadInt(root, "suspendedShopCount") : 0;
        ProviderNotificationCommercialV1? commercial = null;
        if (commercialAllowed && isCommercialAudience)
        {
            var shopSample = LicenseNoticesV1.BoundShopNameSample(ReadStringArray(root, "coveredShopNames"));
            commercial = new ProviderNotificationCommercialV1(
                ReadNullableLong(root, "evidencedTermPricePaise"),
                ReadNullableString(root, "evidencedTermCurrency"),
                ReadBool(root, "renewalQuoteRequired"),
                row.DestinationHint,
                row.Audience == LicenseNoticesV1.AudienceCommercialContact
                    ? "Approved billing contact"
                    : "Verified billing role",
                shopSample,
                coveredShopCount > shopSample.Length);
        }

        return new ProviderNotificationItemV1(
            row.Id,
            row.ScopeKind,
            row.ScopeId,
            row.Audience,
            row.Purpose,
            row.SourceKind,
            row.SourceId,
            row.SourceRevision,
            row.Threshold,
            row.Status,
            row.DueAt <= now,
            row.DueAt,
            row.EffectiveDeadlineAt,
            row.TimeZone,
            row.Locale,
            row.AllowedChannels,
            row.TemplateKey,
            ReadString(root, "guidanceCode", LicenseNoticesV1.GuidanceTrialEnding),
            coveredShopCount,
            ReadBool(root, "paymentDoesNotLiftSuspension"),
            row.PlannedAt,
            commercial,
            suspendedShopCount);
    }

    private static string ReadString(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? fallback
            : fallback;

    private static string? ReadNullableString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static int ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)
            ? value
            : 0;

    private static long? ReadNullableLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var value)
            ? value
            : null;

    private static bool ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var prop) && prop.ValueKind is JsonValueKind.True or JsonValueKind.False
            && prop.GetBoolean();

    private static string[] ReadStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return [];
        var values = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
                values.Add(text);
        }

        return [.. values];
    }
}
