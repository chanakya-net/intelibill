using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Notifications;

/// <summary>
/// Manages account-approved commercial billing contacts with separate holder verification, opt-in,
/// and revocation. Contact consent never grants billing membership, login, or shop data access.
/// </summary>
public sealed class BillingContactConsentHandler(
    IBillingDelegationAccess access,
    IBillingContactStore store,
    TimeProvider clock)
{
    public Task<ErrorOr<BillingContactV1>> ApproveAsync(
        VerifiedBillingDelegation delegation,
        Guid accountId,
        ApproveBillingContactRequestV1 request,
        CancellationToken ct)
    {
        if (delegation.AccountId != accountId
            || delegation.Action != LicenseNoticesV1.ContactApprove
            || accountId == Guid.Empty)
        {
            return Task.FromResult<ErrorOr<BillingContactV1>>(BillingDelegationErrors.Invalid);
        }

        if (!IsValidApproveRequest(request))
            return Task.FromResult<ErrorOr<BillingContactV1>>(NotificationErrors.ContactInvalid);

        return access.ExecuteAsync(
            delegation,
            (scope, token) => store.ApproveAsync(
                accountId,
                scope.ActorId,
                request,
                BillingAccountRules.StorageInstant(clock.GetUtcNow()),
                token),
            ct);
    }

    public Task<ErrorOr<IReadOnlyList<BillingContactV1>>> ListAsync(
        VerifiedBillingDelegation delegation,
        Guid accountId,
        CancellationToken ct)
    {
        if (delegation.AccountId != accountId
            || delegation.Action != LicenseNoticesV1.ContactList
            || accountId == Guid.Empty)
        {
            return Task.FromResult<ErrorOr<IReadOnlyList<BillingContactV1>>>(BillingDelegationErrors.Invalid);
        }

        return access.ExecuteAsync<IReadOnlyList<BillingContactV1>>(
            delegation,
            async (_, token) => ErrorOrFactory.From(await store.ListAsync(accountId, token)),
            ct);
    }

    public Task<ErrorOr<BillingContactV1>> RevokeByOwnerAsync(
        VerifiedBillingDelegation delegation,
        Guid accountId,
        Guid contactId,
        RevokeBillingContactRequestV1 request,
        CancellationToken ct)
    {
        if (delegation.AccountId != accountId
            || delegation.Action != LicenseNoticesV1.ContactOwnerRevoke
            || accountId == Guid.Empty
            || contactId == Guid.Empty)
        {
            return Task.FromResult<ErrorOr<BillingContactV1>>(BillingDelegationErrors.Invalid);
        }

        if (request.OperationKey == Guid.Empty)
            return Task.FromResult<ErrorOr<BillingContactV1>>(NotificationErrors.ContactInvalid);

        return access.ExecuteAsync(
            delegation,
            (scope, token) => store.RevokeByOwnerAsync(
                accountId,
                scope.ActorId,
                contactId,
                request.OperationKey,
                BillingAccountRules.StorageInstant(clock.GetUtcNow()),
                token),
            ct);
    }

    public Task<ErrorOr<BillingContactV1>> ConfirmSelfConsentAsync(
        VerifiedBillingDelegation delegation,
        Guid contactId,
        ConfirmBillingContactConsentRequestV1 request,
        CancellationToken ct)
    {
        if (delegation.AccountId is not null
            || delegation.Action != LicenseNoticesV1.ContactSelfConsent
            || contactId == Guid.Empty)
        {
            return Task.FromResult<ErrorOr<BillingContactV1>>(BillingDelegationErrors.Invalid);
        }

        if (request.OperationKey == Guid.Empty || !IsValidEmail(request.VerifiedEmail))
            return Task.FromResult<ErrorOr<BillingContactV1>>(NotificationErrors.ContactInvalid);

        return access.ExecuteAsync(
            delegation,
            (scope, token) => store.ConfirmSelfConsentAsync(
                scope.ActorId,
                contactId,
                request,
                BillingAccountRules.StorageInstant(clock.GetUtcNow()),
                token),
            ct);
    }

    public Task<ErrorOr<BillingContactV1>> ConfirmStandaloneProofAsync(
        Guid accountId,
        Guid contactId,
        ConfirmBillingContactConsentRequestV1 request,
        CancellationToken ct)
    {
        if (accountId == Guid.Empty
            || contactId == Guid.Empty
            || request.OperationKey == Guid.Empty
            || !IsValidEmail(request.VerifiedEmail)
            || string.IsNullOrWhiteSpace(request.ProofToken))
        {
            return Task.FromResult<ErrorOr<BillingContactV1>>(NotificationErrors.ContactInvalid);
        }

        return store.ConfirmStandaloneProofAsync(
            accountId,
            contactId,
            request,
            BillingAccountRules.StorageInstant(clock.GetUtcNow()),
            ct);
    }

    public Task<ErrorOr<BillingContactV1>> RevokeByHolderAsync(
        VerifiedBillingDelegation delegation,
        Guid contactId,
        RevokeBillingContactRequestV1 request,
        CancellationToken ct)
    {
        if (delegation.AccountId is not null
            || delegation.Action != LicenseNoticesV1.ContactSelfRevoke
            || contactId == Guid.Empty)
        {
            return Task.FromResult<ErrorOr<BillingContactV1>>(BillingDelegationErrors.Invalid);
        }

        if (request.OperationKey == Guid.Empty)
            return Task.FromResult<ErrorOr<BillingContactV1>>(NotificationErrors.ContactInvalid);

        return access.ExecuteAsync(
            delegation,
            (scope, token) => store.RevokeByHolderAsync(
                scope.ActorId,
                contactId,
                request.OperationKey,
                BillingAccountRules.StorageInstant(clock.GetUtcNow()),
                token),
            ct);
    }

    public static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var trimmed = value.Trim();
        if (trimmed.Length is < 5 or > 254 || trimmed.Any(char.IsControl) || trimmed.Any(char.IsWhiteSpace))
            return false;
        var at = trimmed.IndexOf('@');
        return at > 0
            && at == trimmed.LastIndexOf('@')
            && at < trimmed.Length - 2
            && trimmed[(at + 1)..].Contains('.', StringComparison.Ordinal);
    }

    private static bool IsValidApproveRequest(ApproveBillingContactRequestV1 request) =>
        request.OperationKey != Guid.Empty
        && (request.MerchantUserId is null || request.MerchantUserId != Guid.Empty)
        && IsValidEmail(request.Email);
}
