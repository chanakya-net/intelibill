using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient : IMerchantLicenseNotices
{
    public async Task<ErrorOr<ShopLicenseNoticesV1>> ReadShopNoticesAsync(
        MerchantBillingCaller caller,
        Guid shopId,
        Guid? after,
        CancellationToken ct)
    {
        if (shopId == Guid.Empty || after == Guid.Empty)
            return MerchantSubscriptionStatusErrors.ShopScope;

        try
        {
            // Verify live active-shop membership before signing and again before returning.
            if (!await IsLiveShopMemberAsync(caller, shopId, ct))
                return MerchantSubscriptionStatusErrors.ShopScope;

            var result = await RunAsync<ShopLicenseNoticesV1>(
                caller,
                new BillingDelegationIntent(
                    LicenseNoticesV1.ShopRead,
                    HttpMethod.Get,
                    LicenseNoticesV1.ShopNoticesPath(shopId, after),
                    null,
                    []),
                LicenseNoticesV1.ShopResponseLimit,
                async (response, token) =>
                {
                    var payload = await ReadJsonAsync<ShopLicenseNoticesV1>(response, token);
                    return payload is not null && IsValidShopNotices(payload, shopId)
                        ? payload
                        : Unavailable("InvalidShopNoticesContract");
                },
                ct);
            if (result.IsError)
                return result;

            if (!await IsLiveShopMemberAsync(caller, shopId, ct))
                return MerchantSubscriptionStatusErrors.ShopScope;

            return result;
        }
        catch (Exception error) when (error is Npgsql.NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return Unavailable("NoticeShopScopeUnavailable", error);
        }
    }

    public Task<ErrorOr<AccountLicenseNoticesV1>> ReadAccountNoticesAsync(
        MerchantBillingCaller caller,
        Guid accountId,
        Guid? after,
        CancellationToken ct)
    {
        if (accountId == Guid.Empty || after == Guid.Empty)
            return Task.FromResult<ErrorOr<AccountLicenseNoticesV1>>(MerchantBillingErrors.Denied);

        return RunAsync<AccountLicenseNoticesV1>(
            caller,
            new BillingDelegationIntent(
                LicenseNoticesV1.AccountRead,
                HttpMethod.Get,
                LicenseNoticesV1.AccountNoticesPath(accountId, after),
                accountId,
                []),
            LicenseNoticesV1.AccountResponseLimit,
            async (response, token) =>
            {
                var payload = await ReadJsonAsync<AccountLicenseNoticesV1>(response, token);
                return payload is not null && IsValidAccountNotices(payload, accountId)
                    ? payload
                    : Unavailable("InvalidAccountNoticesContract");
            },
            ct);
    }

    public Task<ErrorOr<BillingContactsPageV1>> ListContactsAsync(
        MerchantBillingCaller caller,
        Guid accountId,
        Guid? after,
        CancellationToken ct)
    {
        if (accountId == Guid.Empty || after == Guid.Empty)
            return Task.FromResult<ErrorOr<BillingContactsPageV1>>(MerchantBillingErrors.Denied);

        return RunAsync<BillingContactsPageV1>(
            caller,
            new BillingDelegationIntent(
                LicenseNoticesV1.ContactList,
                HttpMethod.Get,
                LicenseNoticesV1.AccountContactsPath(accountId, after),
                accountId,
                []),
            LicenseNoticesV1.ContactResponseLimit,
            async (response, token) =>
            {
                var payload = await ReadJsonAsync<BillingContactsPageV1>(response, token);
                return payload is not null && IsValidContactsPage(payload, accountId)
                    ? payload
                    : Unavailable("InvalidContactListContract");
            },
            ct);
    }

    public Task<ErrorOr<BillingContactV1>> ApproveContactAsync(
        MerchantBillingCaller caller,
        Guid accountId,
        ApproveBillingContactRequestV1 request,
        CancellationToken ct)
    {
        if (accountId == Guid.Empty)
            return Task.FromResult<ErrorOr<BillingContactV1>>(MerchantBillingErrors.Denied);

        var body = JsonSerializer.SerializeToUtf8Bytes(request, Json);
        return RunAsync<BillingContactV1>(
            caller,
            new BillingDelegationIntent(
                LicenseNoticesV1.ContactApprove,
                HttpMethod.Post,
                LicenseNoticesV1.AccountContactsPath(accountId),
                accountId,
                body),
            LicenseNoticesV1.ContactResponseLimit,
            async (response, token) =>
            {
                var payload = await ReadJsonAsync<BillingContactV1>(response, token);
                return payload is not null && payload.AccountId == accountId
                    ? payload
                    : Unavailable("InvalidContactContract");
            },
            ct);
    }

    public Task<ErrorOr<BillingContactV1>> RevokeContactByOwnerAsync(
        MerchantBillingCaller caller,
        Guid accountId,
        Guid contactId,
        RevokeBillingContactRequestV1 request,
        CancellationToken ct)
    {
        if (accountId == Guid.Empty || contactId == Guid.Empty)
            return Task.FromResult<ErrorOr<BillingContactV1>>(MerchantBillingErrors.Denied);

        var body = JsonSerializer.SerializeToUtf8Bytes(request, Json);
        return RunAsync<BillingContactV1>(
            caller,
            new BillingDelegationIntent(
                LicenseNoticesV1.ContactOwnerRevoke,
                HttpMethod.Post,
                LicenseNoticesV1.ContactOwnerRevokePath(accountId, contactId),
                accountId,
                body),
            LicenseNoticesV1.ContactResponseLimit,
            async (response, token) =>
            {
                var payload = await ReadJsonAsync<BillingContactV1>(response, token);
                return payload is not null && payload.AccountId == accountId && payload.ContactId == contactId
                    ? payload
                    : Unavailable("InvalidContactContract");
            },
            ct);
    }

    public async Task<ErrorOr<BillingContactV1>> ConfirmSelfConsentAsync(
        MerchantBillingCaller caller,
        Guid contactId,
        ConfirmBillingContactConsentRequestV1 request,
        CancellationToken ct)
    {
        if (contactId == Guid.Empty || request.OperationKey == Guid.Empty)
            return Errors.Notifications.ContactInvalid;

        var actor = await db.Users.AsNoTracking()
            .Where(user => user.Id == caller.Session.UserId)
            .Select(user => new { user.IsLoginEnabled, user.IsEmailVerified, user.Email, user.Language })
            .SingleOrDefaultAsync(ct);
        if (actor is not { IsLoginEnabled: true, IsEmailVerified: true } || string.IsNullOrWhiteSpace(actor.Email))
            return Errors.Notifications.VerifiedEmailRequired;

        // Bind self-consent to the live verified merchant email rather than any caller-supplied value.
        var boundRequest = new ConfirmBillingContactConsentRequestV1(
            request.OperationKey,
            actor.Email.Trim().ToLowerInvariant(),
            ProofToken: null,
            request.OptedIn,
            LicenseNoticesV1.NormalizeLocale(string.IsNullOrWhiteSpace(request.Locale) ? actor.Language : request.Locale));
        var body = JsonSerializer.SerializeToUtf8Bytes(boundRequest, Json);
        return await RunAsync<BillingContactV1>(
            caller,
            new BillingDelegationIntent(
                LicenseNoticesV1.ContactSelfConsent,
                HttpMethod.Post,
                LicenseNoticesV1.ContactSelfConsentPath(contactId),
                null,
                body),
            LicenseNoticesV1.ContactResponseLimit,
            async (response, token) =>
            {
                var payload = await ReadJsonAsync<BillingContactV1>(response, token);
                return payload is not null && payload.ContactId == contactId
                    ? payload
                    : Unavailable("InvalidContactContract");
            },
            ct);
    }

    public Task<ErrorOr<BillingContactV1>> RevokeContactByHolderAsync(
        MerchantBillingCaller caller,
        Guid contactId,
        RevokeBillingContactRequestV1 request,
        CancellationToken ct)
    {
        if (contactId == Guid.Empty || request.OperationKey == Guid.Empty)
            return Task.FromResult<ErrorOr<BillingContactV1>>(Errors.Notifications.ContactInvalid);

        var body = JsonSerializer.SerializeToUtf8Bytes(request, Json);
        return RunAsync<BillingContactV1>(
            caller,
            new BillingDelegationIntent(
                LicenseNoticesV1.ContactSelfRevoke,
                HttpMethod.Post,
                LicenseNoticesV1.ContactSelfRevokePath(contactId),
                null,
                body),
            LicenseNoticesV1.ContactResponseLimit,
            async (response, token) =>
            {
                var payload = await ReadJsonAsync<BillingContactV1>(response, token);
                return payload is not null && payload.ContactId == contactId
                    ? payload
                    : Unavailable("InvalidContactContract");
            },
            ct);
    }

    private static Error MapNotificationContactFailure(string? code) => code switch
    {
        "Notifications.InvalidCursor" => Errors.Notifications.InvalidCursor,
        "Notifications.ContactInvalid" => Errors.Notifications.ContactInvalid,
        "Notifications.ContactNotFound" => Errors.Notifications.ContactNotFound,
        "Notifications.ContactConflict" => Errors.Notifications.ContactConflict,
        "Notifications.ContactProofInvalid" => Errors.Notifications.ContactProofInvalid,
        "Notifications.ContactOperationMismatch" => Errors.Notifications.ContactOperationMismatch,
        _ => MerchantBillingErrors.Unavailable,
    };

    private static bool IsValidShopNotices(ShopLicenseNoticesV1 payload, Guid shopId) =>
        payload.SchemaVersion == LicenseNoticesV1.SchemaVersion
        && payload.ShopId == shopId
        && payload.Notices is { Length: <= LicenseNoticesV1.PageSize }
        && payload.Notices.All(item => item.NoticeId != Guid.Empty && item.ShopId == shopId);

    private static bool IsValidAccountNotices(AccountLicenseNoticesV1 payload, Guid accountId) =>
        payload.SchemaVersion == LicenseNoticesV1.SchemaVersion
        && payload.AccountId == accountId
        && payload.Notices is { Length: <= LicenseNoticesV1.PageSize }
        && payload.Notices.All(item => item.NoticeId != Guid.Empty && item.AccountId == accountId);

    private static bool IsValidContactsPage(BillingContactsPageV1 payload, Guid accountId) =>
        payload.SchemaVersion == LicenseNoticesV1.SchemaVersion
        && payload.AccountId == accountId
        && payload.Items is { Length: <= LicenseNoticesV1.ContactPageSize }
        && payload.TotalCount >= payload.Items.Length
        && (payload.NextCursor is null || (payload.NextCursor != Guid.Empty && payload.Items.Length > 0))
        && payload.Items.All(item => item.ContactId != Guid.Empty && item.AccountId == accountId);
}
