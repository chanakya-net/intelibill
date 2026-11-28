using System.Data;
using System.Security.Cryptography;
using ErrorOr;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed class BillingContactStore(
    LicensingDbContext db,
    NotificationDestinationProtector protector) : IBillingContactStore
{
    private static readonly TimeSpan ProofLifetime = TimeSpan.FromHours(24);

    public async Task<ErrorOr<BillingContactV1>> ApproveAsync(
        Guid accountId,
        Guid ownerActorId,
        ApproveBillingContactRequestV1 request,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var normalizedEmail = NotificationPlanner.NormalizeEmail(request.Email!);
        var destinationHash = NotificationDestinationProtector.HashEmail(normalizedEmail);
        var locale = LicenseNoticesV1.NormalizeLocale(request.Locale);

        var existingByOp = await ReadContactByOperationAsync(accountId, request.OperationKey, ct);
        if (existingByOp is not null)
        {
            var same = existingByOp.MerchantUserId == request.MerchantUserId
                && existingByOp.DestinationHash.AsSpan().SequenceEqual(destinationHash);
            return same ? ToContract(existingByOp) : NotificationErrors.ContactOperationMismatch;
        }

        var conflict = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT FROM public.billing_notice_contacts
                WHERE account_id = {accountId}
                  AND revoked_at IS NULL
                  AND (destination_hash = {destinationHash}
                       OR ({request.MerchantUserId}::uuid IS NOT NULL AND merchant_user_id = {request.MerchantUserId}::uuid))
            ) AS "Value"
            """).SingleAsync(ct);
        if (conflict)
            return NotificationErrors.ContactConflict;

        var contactId = Guid.CreateVersion7(now);
        var protectedDestination = protector.ProtectContactDestination(accountId, contactId, normalizedEmail);
        var hint = NotificationPlanner.MaskEmail(normalizedEmail);
        string? proofToken = null;
        byte[]? proofHash = null;
        DateTimeOffset? proofExpiry = null;
        if (request.MerchantUserId is null)
        {
            (proofToken, proofHash) = protector.CreateStandaloneProof(accountId, contactId, destinationHash);
            proofExpiry = now.Add(ProofLifetime);
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.billing_notice_contacts (
                id, account_id, merchant_user_id, destination_hash, protected_destination,
                destination_hint, locale, approved_by_actor_id, approved_at, operation_key,
                verification_token_hash, verification_expires_at, verified_at, opted_in_at,
                revoked_at, revoked_by_actor_id, revision)
            VALUES (
                {contactId}, {accountId}, {request.MerchantUserId}, {destinationHash}, {protectedDestination},
                {hint}, {locale}, {ownerActorId}, {now}, {request.OperationKey},
                {proofHash}, {proofExpiry}, NULL, NULL, NULL, NULL, 1)
            """, ct);

        await AppendEventAsync(contactId, accountId, "Approved", ownerActorId, revision: 1, now, ct);
        return new BillingContactV1(
            contactId,
            accountId,
            request.MerchantUserId,
            hint,
            locale,
            now,
            VerifiedAt: null,
            OptedInAt: null,
            RevokedAt: null,
            Revision: 1,
            Eligible: false,
            VerificationProofToken: proofToken);
    }

    public async Task<IReadOnlyList<BillingContactV1>> ListAsync(Guid accountId, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<ContactRow>($"""
            SELECT id, account_id, merchant_user_id, destination_hash, destination_hint, locale,
                approved_at, verification_token_hash, verification_expires_at, verified_at,
                opted_in_at, revoked_at, revision
            FROM public.billing_notice_contacts
            WHERE account_id = {accountId}
            ORDER BY approved_at ASC, id ASC
            """).ToListAsync(ct);
        return [.. rows.Select(ToContract)];
    }

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

        return await RevokeLockedAsync(row, ownerActorId, now, ct);
    }

    public async Task<ErrorOr<BillingContactV1>> ConfirmSelfConsentAsync(
        Guid actorId,
        Guid contactId,
        ConfirmBillingContactConsentRequestV1 request,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<ContactRow>($"""
            SELECT id, account_id, merchant_user_id, destination_hash, destination_hint, locale,
                approved_at, verification_token_hash, verification_expires_at, verified_at,
                opted_in_at, revoked_at, revision
            FROM public.billing_notice_contacts
            WHERE id = {contactId} AND merchant_user_id = {actorId}
            FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        if (row is null || row.RevokedAt is not null)
            return NotificationErrors.ContactNotFound;

        var emailHash = NotificationDestinationProtector.HashEmail(
            NotificationPlanner.NormalizeEmail(request.VerifiedEmail!));
        if (!CryptographicOperations.FixedTimeEquals(row.DestinationHash, emailHash))
            return NotificationErrors.ContactProofInvalid;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.billing_account_id', {row.AccountId.ToString("D")}, true)",
            ct);
        return await ApplyConsentAsync(row, actorId, request.OptedIn, request.Locale, now, ct);
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
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.billing_account_id', {accountId.ToString("D")}, true)",
                ct);
            await BillingAccountLock.TakeAsync(db, accountId, ct);

            var row = await LockContactForAccountAsync(accountId, contactId, ct);
            if (row is null
                || row.MerchantUserId is not null
                || row.RevokedAt is not null
                || row.VerificationTokenHash is null
                || row.VerificationExpiresAt is not { } expiry
                || now >= expiry)
            {
                return NotificationErrors.ContactProofInvalid;
            }

            var emailHash = NotificationDestinationProtector.HashEmail(
                NotificationPlanner.NormalizeEmail(request.VerifiedEmail!));
            if (!CryptographicOperations.FixedTimeEquals(row.DestinationHash, emailHash))
                return NotificationErrors.ContactProofInvalid;

            var presentedHash = NotificationDestinationProtector.HashProof(
                accountId,
                contactId,
                row.DestinationHash,
                request.ProofToken!);
            if (!CryptographicOperations.FixedTimeEquals(row.VerificationTokenHash, presentedHash))
                return NotificationErrors.ContactProofInvalid;

            var result = await ApplyConsentAsync(row, actorId: null, request.OptedIn, request.Locale, now, ct);
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
        var row = await db.Database.SqlQuery<ContactRow>($"""
            SELECT id, account_id, merchant_user_id, destination_hash, destination_hint, locale,
                approved_at, verification_token_hash, verification_expires_at, verified_at,
                opted_in_at, revoked_at, revision
            FROM public.billing_notice_contacts
            WHERE id = {contactId} AND merchant_user_id = {actorId}
            FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        if (row is null)
            return NotificationErrors.ContactNotFound;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.billing_account_id', {row.AccountId.ToString("D")}, true)",
            ct);
        return await RevokeLockedAsync(row, actorId, now, ct);
    }

    private async Task<ErrorOr<BillingContactV1>> ApplyConsentAsync(
        ContactRow row,
        Guid? actorId,
        bool optedIn,
        string? requestedLocale,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var locale = string.IsNullOrWhiteSpace(requestedLocale)
            ? row.Locale
            : LicenseNoticesV1.NormalizeLocale(requestedLocale);
        var nextRevision = row.Revision + 1;
        var verifiedAt = row.VerifiedAt ?? now;
        DateTimeOffset? optedInAt = optedIn ? row.OptedInAt ?? now : null;

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

        if (row.VerifiedAt is null)
            await AppendEventAsync(row.Id, row.AccountId, "Verified", actorId, nextRevision, now, ct);
        if (optedIn && row.OptedInAt is null)
            await AppendEventAsync(row.Id, row.AccountId, "OptedIn", actorId, nextRevision, now, ct);

        return new BillingContactV1(
            row.Id,
            row.AccountId,
            row.MerchantUserId,
            row.DestinationHint,
            locale,
            row.ApprovedAt.ToUniversalTime(),
            verifiedAt.ToUniversalTime(),
            optedInAt?.ToUniversalTime(),
            RevokedAt: null,
            nextRevision,
            Eligible: optedInAt is not null);
    }

    private async Task<ErrorOr<BillingContactV1>> RevokeLockedAsync(
        ContactRow row,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (row.RevokedAt is not null)
            return ToContract(row);

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

    private Task<ContactRow?> ReadContactByOperationAsync(Guid accountId, Guid operationKey, CancellationToken ct) =>
        db.Database.SqlQuery<ContactRow>($"""
            SELECT id, account_id, merchant_user_id, destination_hash, destination_hint, locale,
                approved_at, verification_token_hash, verification_expires_at, verified_at,
                opted_in_at, revoked_at, revision
            FROM public.billing_notice_contacts
            WHERE account_id = {accountId} AND operation_key = {operationKey}
            """).SingleOrDefaultAsync(ct);

    private Task<ContactRow?> LockContactForAccountAsync(Guid accountId, Guid contactId, CancellationToken ct) =>
        db.Database.SqlQuery<ContactRow>($"""
            SELECT id, account_id, merchant_user_id, destination_hash, destination_hint, locale,
                approved_at, verification_token_hash, verification_expires_at, verified_at,
                opted_in_at, revoked_at, revision
            FROM public.billing_notice_contacts
            WHERE account_id = {accountId} AND id = {contactId}
            FOR UPDATE
            """).SingleOrDefaultAsync(ct);

    private Task<int> AppendEventAsync(
        Guid contactId,
        Guid accountId,
        string kind,
        Guid? actorId,
        int revision,
        DateTimeOffset recordedAt,
        CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.billing_notice_contact_events
                (id, contact_id, account_id, event_kind, actor_id, revision, recorded_at)
            VALUES
                ({Guid.CreateVersion7(recordedAt)}, {contactId}, {accountId}, {kind}, {actorId}, {revision}, {recordedAt})
            """, ct);

    private static BillingContactV1 ToContract(ContactRow row) => new(
        row.Id,
        row.AccountId,
        row.MerchantUserId,
        row.DestinationHint,
        row.Locale,
        row.ApprovedAt.ToUniversalTime(),
        row.VerifiedAt?.ToUniversalTime(),
        row.OptedInAt?.ToUniversalTime(),
        row.RevokedAt?.ToUniversalTime(),
        row.Revision,
        Eligible: row.VerifiedAt is not null && row.OptedInAt is not null && row.RevokedAt is null);

    private sealed record ContactRow(
        Guid Id,
        Guid AccountId,
        Guid? MerchantUserId,
        byte[] DestinationHash,
        string DestinationHint,
        string Locale,
        DateTimeOffset ApprovedAt,
        byte[]? VerificationTokenHash,
        DateTimeOffset? VerificationExpiresAt,
        DateTimeOffset? VerifiedAt,
        DateTimeOffset? OptedInAt,
        DateTimeOffset? RevokedAt,
        int Revision);
}
