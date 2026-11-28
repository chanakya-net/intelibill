using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Notifications;

/// <summary>
/// Separate merchant shop, merchant billing-account, and provider preview reads over persisted
/// <see cref="NotificationJob"/> rows. Strictly read-only: never writes jobs or claims delivery.
/// </summary>
public sealed class ReadLicenseNoticesHandler(
    IBillingDelegationAccess access,
    ISubscriptionStatusStore statusStore,
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

        return access.ExecuteAsync<ShopLicenseNoticesV1>(delegation, async (scope, token) =>
        {
            var now = SubscriptionStatusProjection.EvaluationInstant(statusClock);
            await statusStore.UseShopAsync(shopId, token);
            var accountId = await reader.ReadCurrentShopAccountAsync(shopId, token);
            if (accountId is not { } currentAccount)
                return new ShopLicenseNoticesV1(LicenseNoticesV1.SchemaVersion, shopId, now, [], null, null);

            var page = await reader.ReadDueShopNoticesAsync(
                currentAccount,
                shopId,
                scope.ActorId,
                now,
                after,
                LicenseNoticesV1.PageSize,
                token);
            var items = page.Rows.Select(ProjectShopNotice).ToArray();
            var nextCursor = page.HasMore && items.Length > 0 ? items[^1].NoticeId : (Guid?)null;
            return new ShopLicenseNoticesV1(
                LicenseNoticesV1.SchemaVersion,
                shopId,
                now,
                items,
                page.NextDueAt,
                nextCursor);
        }, ct);
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
        return access.ExecuteAsync<AccountLicenseNoticesV1>(delegation, async (scope, token) =>
        {
            var now = SubscriptionStatusProjection.EvaluationInstant(statusClock);
            var page = await reader.ReadDueAccountNoticesAsync(
                accountId,
                scope.ActorId,
                now,
                after,
                LicenseNoticesV1.PageSize,
                token);
            var items = page.Rows.Select(ProjectAccountNotice).ToArray();
            var nextCursor = page.HasMore && items.Length > 0 ? items[^1].NoticeId : (Guid?)null;
            return new AccountLicenseNoticesV1(
                LicenseNoticesV1.SchemaVersion,
                accountId,
                now,
                items,
                page.NextDueAt,
                nextCursor);
        }, ct);
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

    private static ShopLicenseNoticeItemV1 ProjectShopNotice(NotificationJobReadRow row)
    {
        using var args = JsonDocument.Parse(row.TemplateArguments);
        var root = args.RootElement;
        return new ShopLicenseNoticeItemV1(
            row.Id,
            row.ScopeId,
            row.Purpose,
            row.Threshold,
            ReadString(root, "accessState", "Active"),
            ReadString(root, "guidanceCode", LicenseNoticesV1.GuidanceTrialEnding),
            row.TemplateKey,
            row.DueAt,
            row.EffectiveDeadlineAt,
            row.TimeZone,
            row.Locale,
            ReadString(root, "shopName", string.Empty));
    }

    private static AccountLicenseNoticeItemV1 ProjectAccountNotice(NotificationJobReadRow row)
    {
        using var args = JsonDocument.Parse(row.TemplateArguments);
        var root = args.RootElement;
        return new AccountLicenseNoticeItemV1(
            row.Id,
            row.AccountId,
            row.Purpose,
            row.SourceKind,
            row.SourceId,
            row.Threshold,
            ReadString(root, "accessState", "Active"),
            ReadString(root, "guidanceCode", LicenseNoticesV1.GuidancePaidRenewalDue),
            row.TemplateKey,
            row.DueAt,
            row.EffectiveDeadlineAt,
            row.TimeZone,
            row.Locale,
            row.AllowedChannels,
            ReadInt(root, "coveredShopCount"),
            ReadStringArray(root, "coveredShopNames"),
            ReadNullableLong(root, "evidencedTermPricePaise"),
            ReadNullableString(root, "evidencedTermCurrency"),
            ReadBool(root, "renewalQuoteRequired"),
            ReadBool(root, "paymentDoesNotLiftSuspension"));
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
        ProviderNotificationCommercialV1? commercial = null;
        if (commercialAllowed && isCommercialAudience)
        {
            var recipientReference = row.Audience == LicenseNoticesV1.AudienceCommercialContact
                ? "Approved billing contact"
                : "Verified billing role";
            commercial = new ProviderNotificationCommercialV1(
                ReadNullableLong(root, "evidencedTermPricePaise"),
                ReadNullableString(root, "evidencedTermCurrency"),
                ReadBool(root, "renewalQuoteRequired"),
                row.DestinationHint,
                recipientReference,
                ReadStringArray(root, "coveredShopNames"));
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
            commercial);
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
