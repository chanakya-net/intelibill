using System.Data;
using System.Security.Cryptography;
using ErrorOr;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed partial class BillingContactStore
{
    public async Task<ErrorOr<BillingContactV1>> RevokeByOwnerAsync(
        Guid accountId,
        Guid ownerActorId,
        Guid contactId,
        Guid operationKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var row = await LockContactForAccountAsync(accountId, contactId, ct);
        if (row is null)
            return NotificationErrors.ContactNotFound;

        return await RevokeLockedAsync(row, ActionOwnerRevoke, ownerActorId, operationKey, now, ct);
    }

    public async Task<ErrorOr<BillingContactV1>> ConfirmSelfConsentAsync(
        Guid actorId,
        Guid contactId,
        ConfirmBillingContactConsentRequestV1 request,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var accountId = await ResolveHolderAccountAsync(actorId, contactId, ct);
        if (accountId is not { } targetAccount)
            return NotificationErrors.ContactNotFound;

        await EnterAccountLockAsync(targetAccount, ct);
        var row = await LockContactForAccountAsync(targetAccount, contactId, ct);
        if (row is null || row.MerchantUserId != actorId)
            return NotificationErrors.ContactNotFound;

        var normalizedEmail = NotificationPlanner.NormalizeEmail(request.VerifiedEmail!);
        var emailHash = NotificationDestinationProtector.HashEmail(normalizedEmail);
        if (!CryptographicOperations.FixedTimeEquals(row.DestinationHash, emailHash))
            return NotificationErrors.ContactProofInvalid;

        var localeToken = NormalizeRequestedLocaleToken(request.Locale);
        var payloadHash = HashOperationPayload(
            ActionSelfConsent,
            actorId,
            row.AccountId,
            row.Id,
            row.MerchantUserId,
            normalizedEmail,
            request.OptedIn,
            localeToken,
            proofDigestHex: null);
        return await ApplyConsentWithReplayAsync(
            row,
            ActionSelfConsent,
            actorId,
            request.OperationKey,
            request.OptedIn,
            request.Locale,
            payloadHash,
            now,
            ct);
    }

    public async Task<ErrorOr<BillingContactV1>> ConfirmStandaloneProofAsync(
        Guid accountId,
        Guid contactId,
        ConfirmBillingContactConsentRequestV1 request,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.SetLockTimeoutAsync(LockWait.Brief, ct);
            await EnterAccountLockAsync(accountId, ct);

            var result = await ExecuteStandaloneProofLockedAsync(accountId, contactId, request, now, ct);
            if (!result.IsError)
                await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return NotificationErrors.Unavailable;
        }
    }

    public async Task<ErrorOr<BillingContactV1>> RevokeByHolderAsync(
        Guid actorId,
        Guid contactId,
        Guid operationKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var accountId = await ResolveHolderAccountAsync(actorId, contactId, ct);
        if (accountId is not { } targetAccount)
            return NotificationErrors.ContactNotFound;

        await EnterAccountLockAsync(targetAccount, ct);
        var row = await LockContactForAccountAsync(targetAccount, contactId, ct);
        if (row is null || row.MerchantUserId != actorId)
            return NotificationErrors.ContactNotFound;

        return await RevokeLockedAsync(row, ActionHolderRevoke, actorId, operationKey, now, ct);
    }

    private async Task<ErrorOr<BillingContactV1>> ExecuteStandaloneProofLockedAsync(
        Guid accountId,
        Guid contactId,
        ConfirmBillingContactConsentRequestV1 request,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var row = await LockContactForAccountAsync(accountId, contactId, ct);
        if (row is null || row.MerchantUserId is not null)
            return NotificationErrors.ContactProofInvalid;

        var normalizedEmail = NotificationPlanner.NormalizeEmail(request.VerifiedEmail!);
        var emailHash = NotificationDestinationProtector.HashEmail(normalizedEmail);
        if (!CryptographicOperations.FixedTimeEquals(row.DestinationHash, emailHash))
            return NotificationErrors.ContactProofInvalid;

        var presentedHash = NotificationDestinationProtector.HashProof(
            accountId, contactId, row.DestinationHash, request.ProofToken!);
        var payloadHash = HashOperationPayload(
            ActionStandaloneConsent,
            actorId: null,
            accountId,
            contactId,
            merchantUserId: null,
            normalizedEmail,
            request.OptedIn,
            NormalizeRequestedLocaleToken(request.Locale),
            Convert.ToHexStringLower(presentedHash));

        var existingOp = await ReadOperationAsync(accountId, ActionStandaloneConsent, request.OperationKey, ct);
        if (existingOp is not null)
        {
            return MatchesOperation(existingOp, contactId, actorId: null, payloadHash)
                ? ToContract(row)
                : NotificationErrors.ContactOperationMismatch;
        }

        if (!IsStandaloneProofLiveAndValid(row, presentedHash, now))
            return NotificationErrors.ContactProofInvalid;

        return await ApplyNewConsentAsync(
            row, ActionStandaloneConsent, null, request.OperationKey, request.OptedIn, request.Locale, payloadHash, now, ct);
    }

    private static bool IsStandaloneProofLiveAndValid(ContactRow row, byte[] presentedHash, DateTimeOffset now) =>
        row.RevokedAt is null
        && row.VerificationTokenHash is not null
        && row.VerificationExpiresAt is { } expiry
        && now < expiry
        && CryptographicOperations.FixedTimeEquals(row.VerificationTokenHash, presentedHash);

    private async Task<ErrorOr<BillingContactV1>> ApplyConsentWithReplayAsync(
        ContactRow row,
        string action,
        Guid? actorId,
        Guid operationKey,
        bool optedIn,
        string? requestedLocale,
        byte[] payloadHash,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existingOp = await ReadOperationAsync(row.AccountId, action, operationKey, ct);
        if (existingOp is not null)
        {
            return MatchesOperation(existingOp, row.Id, actorId, payloadHash)
                ? ToContract(row)
                : NotificationErrors.ContactOperationMismatch;
        }

        if (row.RevokedAt is not null)
            return NotificationErrors.ContactNotFound;

        return await ApplyNewConsentAsync(
            row,
            action,
            actorId,
            operationKey,
            optedIn,
            requestedLocale,
            payloadHash,
            now,
            ct);
    }

    private async Task<BillingContactV1> ApplyNewConsentAsync(
        ContactRow row,
        string action,
        Guid? actorId,
        Guid operationKey,
        bool optedIn,
        string? requestedLocale,
        byte[] payloadHash,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var locale = string.IsNullOrWhiteSpace(requestedLocale)
            ? row.Locale
            : LicenseNoticesV1.NormalizeLocale(requestedLocale);
        var verifiedAt = row.VerifiedAt ?? now;
        DateTimeOffset? optedInAt = optedIn ? row.OptedInAt ?? now : null;
        var changed = row.VerifiedAt != verifiedAt
            || row.OptedInAt != optedInAt
            || !string.Equals(row.Locale, locale, StringComparison.Ordinal)
            || row.VerificationTokenHash is not null;
        var nextRevision = changed ? row.Revision + 1 : row.Revision;

        if (changed)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE public.billing_notice_contacts
                SET verified_at = {verifiedAt},
                    opted_in_at = {optedInAt},
                    verification_token_hash = NULL,
                    verification_expires_at = NULL,
                    locale = {locale},
                    revision = {nextRevision}
                WHERE id = {row.Id} AND account_id = {row.AccountId}
                """, ct);
        }

        await AppendConsentEventsAsync(row, actorId, optedIn, nextRevision, now, ct);
        await RecordOperationAsync(
            row.AccountId, action, operationKey, row.Id, actorId, payloadHash, nextRevision, now, ct);

        return new BillingContactV1(
            row.Id, row.AccountId, row.MerchantUserId, row.DestinationHint, locale,
            row.ApprovedAt.ToUniversalTime(), verifiedAt.ToUniversalTime(), optedInAt?.ToUniversalTime(),
            null, nextRevision, optedInAt is not null);
    }

    private async Task AppendConsentEventsAsync(
        ContactRow row,
        Guid? actorId,
        bool optedIn,
        int revision,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (row.VerifiedAt is null)
            await AppendEventAsync(row.Id, row.AccountId, "Verified", actorId, revision, now, ct);
        if (optedIn && row.OptedInAt is null)
            await AppendEventAsync(row.Id, row.AccountId, "OptedIn", actorId, revision, now, ct);
        if (!optedIn && row.OptedInAt is not null)
            await AppendEventAsync(row.Id, row.AccountId, "OptedOut", actorId, revision, now, ct);
    }

    private async Task<ErrorOr<BillingContactV1>> RevokeLockedAsync(
        ContactRow row,
        string action,
        Guid actorId,
        Guid operationKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var payloadHash = HashOperationPayload(
            action,
            actorId,
            row.AccountId,
            row.Id,
            row.MerchantUserId,
            normalizedEmail: null,
            optedIn: null,
            normalizedLocaleToken: null,
            proofDigestHex: null);
        var existingOp = await ReadOperationAsync(row.AccountId, action, operationKey, ct);
        if (existingOp is not null)
        {
            return MatchesOperation(existingOp, row.Id, actorId, payloadHash)
                ? ToContract(row)
                : NotificationErrors.ContactOperationMismatch;
        }

        if (row.RevokedAt is not null)
        {
            await RecordOperationAsync(row.AccountId, action, operationKey, row.Id, actorId, payloadHash, row.Revision, now, ct);
            return ToContract(row);
        }

        var nextRevision = row.Revision + 1;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public.billing_notice_contacts
            SET revoked_at = {now},
                revoked_by_actor_id = {actorId},
                verification_token_hash = NULL,
                verification_expires_at = NULL,
                revision = {nextRevision}
            WHERE id = {row.Id} AND account_id = {row.AccountId}
            """, ct);

        await AppendEventAsync(row.Id, row.AccountId, "Revoked", actorId, nextRevision, now, ct);
        await RecordOperationAsync(row.AccountId, action, operationKey, row.Id, actorId, payloadHash, nextRevision, now, ct);
        return new BillingContactV1(
            row.Id,
            row.AccountId,
            row.MerchantUserId,
            row.DestinationHint,
            row.Locale,
            row.ApprovedAt.ToUniversalTime(),
            row.VerifiedAt?.ToUniversalTime(),
            row.OptedInAt?.ToUniversalTime(),
            now,
            nextRevision,
            Eligible: false);
    }

    private async Task<Guid?> ResolveHolderAccountAsync(Guid actorId, Guid contactId, CancellationToken ct)
    {
        var found = await db.Database.SqlQuery<Guid>($"""
            SELECT account_id AS "Value"
            FROM public.billing_notice_contacts
            WHERE id = {contactId} AND merchant_user_id = {actorId}
            """).ToListAsync(ct);
        return found is [var accountId] ? accountId : null;
    }

    private async Task EnterAccountLockAsync(Guid accountId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.billing_account_id', {accountId.ToString("D")}, true)",
            ct);
        await BillingAccountLock.TakeAsync(db, accountId, ct);
    }

    private static string NormalizeRequestedLocaleToken(string? requestedLocale) =>
        string.IsNullOrWhiteSpace(requestedLocale) ? string.Empty : LicenseNoticesV1.NormalizeLocale(requestedLocale);

    private static bool MatchesOperation(OperationRow existing, Guid contactId, Guid? actorId, byte[] payloadHash) =>
        existing.ContactId == contactId
        && existing.ActorId == actorId
        && CryptographicOperations.FixedTimeEquals(existing.PayloadHash, payloadHash);
}
