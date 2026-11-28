using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Notifications;

public sealed record StandaloneMailboxProof(
    Guid AccountId,
    Guid ContactId,
    string NormalizedEmail,
    string ProofToken,
    DateTimeOffset ExpiresAt);

public sealed record ApprovedBillingContactResult(
    BillingContactV1 Contact,
    StandaloneMailboxProof? PendingMailboxProof);

/// <summary>
/// Out-of-band mailbox proof handoff seam. Production binds a no-op until outbound email delivery
/// (#925) dispatches mailbox proofs directly to the address holder; proofs never return to the approver.
/// </summary>
public interface IBillingContactMailboxProofDispatch
{
    void OnStandaloneProofIssued(StandaloneMailboxProof proof);
}

public sealed class NoOpBillingContactMailboxProofDispatch : IBillingContactMailboxProofDispatch
{
    public void OnStandaloneProofIssued(StandaloneMailboxProof proof)
    {
    }
}

/// <summary>
/// Manages account-approved commercial billing contacts with separate holder verification, opt-in,
/// and revocation. Contact consent never grants billing membership, login, or shop data access.
/// </summary>
public sealed class BillingContactConsentHandler(
    IBillingDelegationAccess access,
    IBillingContactStore store,
    IBillingContactMailboxProofDispatch proofDispatch,
    TimeProvider clock)
{
    public async Task<ErrorOr<BillingContactV1>> ApproveAsync(
        VerifiedBillingDelegation delegation,
        Guid accountId,
        ApproveBillingContactRequestV1 request,
        CancellationToken ct)
    {
        if (delegation.AccountId != accountId
            || delegation.Action != LicenseNoticesV1.ContactApprove
            || accountId == Guid.Empty)
        {
            return BillingDelegationErrors.Invalid;
        }

        if (!IsValidApproveRequest(request))
            return NotificationErrors.ContactInvalid;

        var saved = await access.ExecuteAsync(
            delegation,
            (scope, token) => store.ApproveAsync(
                accountId,
                scope.ActorId,
                request,
                BillingAccountRules.StorageInstant(clock.GetUtcNow()),
                token),
            ct);
        if (saved.IsError)
            return saved.Errors;

        if (saved.Value.PendingMailboxProof is { } proof)
            proofDispatch.OnStandaloneProofIssued(proof);

        return saved.Value.Contact;
    }

    public Task<ErrorOr<BillingContactsPageV1>> ListAsync(
        VerifiedBillingDelegation delegation,
        Guid accountId,
        Guid? after,
        CancellationToken ct)
    {
        if (delegation.AccountId != accountId
            || delegation.Action != LicenseNoticesV1.ContactList
            || accountId == Guid.Empty
            || after == Guid.Empty)
        {
            return Task.FromResult<ErrorOr<BillingContactsPageV1>>(BillingDelegationErrors.Invalid);
        }

        return access.ExecuteAsync(
            delegation,
            (_, token) => store.ListAsync(accountId, after, LicenseNoticesV1.ContactPageSize, token),
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
