using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed partial class BillingContactStore(
    LicensingDbContext db,
    NotificationDestinationProtector protector) : IBillingContactStore
{
    private const string ActionApprove = "Approve";
    private const string ActionSelfConsent = "SelfConsent";
    private const string ActionStandaloneConsent = "StandaloneConsent";
    private const string ActionOwnerRevoke = "OwnerRevoke";
    private const string ActionHolderRevoke = "HolderRevoke";
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
        var payloadHash = HashOperationPayload(
            ActionApprove,
            ownerActorId,
            accountId,
            contactId: Guid.Empty,
            request.MerchantUserId,
            normalizedEmail,
            optedIn: null,
            locale,
            proofDigestHex: null);

        var replay = await CheckExistingApproveAsync(accountId, ownerActorId, request, payloadHash, ct);
        if (replay is not null)
            return replay.Value;

        if (await HasActiveConflictAsync(accountId, destinationHash, request.MerchantUserId, ct))
            return NotificationErrors.ContactConflict;

        return await InsertApprovedContactAsync(
            accountId,
            ownerActorId,
            request,
            normalizedEmail,
            destinationHash,
            locale,
            payloadHash,
            now,
            ct);
    }

    public async Task<ErrorOr<BillingContactsPageV1>> ListAsync(
        Guid accountId,
        Guid? after,
        int take,
        CancellationToken ct)
    {
        DateTimeOffset? cursorApprovedAt = null;
        if (after is { } cursorId)
        {
            var found = await db.Database.SqlQuery<DateTimeOffset>($"""
                SELECT approved_at AS "Value"
                FROM public.billing_notice_contacts
                WHERE account_id = {accountId} AND id = {cursorId}
                """).ToListAsync(ct);
            if (found is not [var value])
                return NotificationErrors.InvalidCursor;
            cursorApprovedAt = value;
        }

        var totalCount = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value"
            FROM public.billing_notice_contacts
            WHERE account_id = {accountId}
            """).SingleAsync(ct);

        var clamped = Math.Clamp(take, 1, LicenseNoticesV1.ContactPageSize);
        var rows = await db.Database.SqlQuery<ContactRow>($"""
            SELECT id, account_id, merchant_user_id, destination_hash, destination_hint, locale,
                approved_at, verification_token_hash, verification_expires_at, verified_at,
                opted_in_at, revoked_at, revision
            FROM public.billing_notice_contacts
            WHERE account_id = {accountId}
              AND ({after}::uuid IS NULL OR (approved_at, id) > ({cursorApprovedAt}, {after}::uuid))
            ORDER BY approved_at ASC, id ASC
            LIMIT {clamped + 1}
            """).ToListAsync(ct);

        var hasMore = rows.Count > clamped;
        var items = rows.Take(clamped).Select(ToContract).ToArray();
        var nextCursor = hasMore && items.Length > 0 ? items[^1].ContactId : (Guid?)null;
        return new BillingContactsPageV1(
            LicenseNoticesV1.SchemaVersion,
            accountId,
            totalCount,
            items,
            nextCursor);
    }

    private async Task<ErrorOr<BillingContactV1>?> CheckExistingApproveAsync(
        Guid accountId,
        Guid ownerActorId,
        ApproveBillingContactRequestV1 request,
        byte[] payloadHash,
        CancellationToken ct)
    {
        var existingOp = await ReadOperationAsync(accountId, ActionApprove, request.OperationKey, ct);
        if (existingOp is null)
            return null;

        if (existingOp.ActorId != ownerActorId
            || !CryptographicOperations.FixedTimeEquals(existingOp.PayloadHash, payloadHash))
        {
            return NotificationErrors.ContactOperationMismatch;
        }

        var existingContact = await ReadContactForAccountAsync(accountId, existingOp.ContactId, ct);
        return existingContact is null
            ? NotificationErrors.ContactNotFound
            : ToContract(existingContact);
    }

    private Task<bool> HasActiveConflictAsync(
        Guid accountId,
        byte[] destinationHash,
        Guid? merchantUserId,
        CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT FROM public.billing_notice_contacts
                WHERE account_id = {accountId}
                  AND revoked_at IS NULL
                  AND (destination_hash = {destinationHash}
                       OR ({merchantUserId}::uuid IS NOT NULL AND merchant_user_id = {merchantUserId}::uuid))
            ) AS "Value"
            """).SingleAsync(ct);

    private async Task<BillingContactV1> InsertApprovedContactAsync(
        Guid accountId,
        Guid ownerActorId,
        ApproveBillingContactRequestV1 request,
        string normalizedEmail,
        byte[] destinationHash,
        string locale,
        byte[] payloadHash,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var contactId = Guid.CreateVersion7(now);
        var protectedDestination = protector.ProtectContactDestination(accountId, contactId, normalizedEmail);
        var hint = NotificationPlanner.MaskEmail(normalizedEmail);
        var (proofToken, proofHash, proofExpiry) = PrepareStandaloneProof(
            accountId,
            contactId,
            request.MerchantUserId,
            destinationHash,
            now);

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
        await RecordOperationAsync(
            accountId, ActionApprove, request.OperationKey, contactId, ownerActorId, payloadHash, 1, now, ct);

        if (proofToken is not null)
            await EnqueueProofAsync(accountId, contactId, proofToken, now, ct);
        return new BillingContactV1(
            contactId, accountId, request.MerchantUserId, hint, locale, now, null, null, null, 1, false);
    }

    private (string? Token, byte[]? Hash, DateTimeOffset? Expiry) PrepareStandaloneProof(
        Guid accountId,
        Guid contactId,
        Guid? merchantUserId,
        byte[] destinationHash,
        DateTimeOffset now)
    {
        if (merchantUserId is not null)
            return (null, null, null);

        var (proofToken, createdHash) = protector.CreateStandaloneProof(accountId, contactId, destinationHash);
        var expiry = now.Add(ProofLifetime);
        return (proofToken, createdHash, expiry);
    }

    private Task<int> EnqueueProofAsync(Guid accountId, Guid contactId, string token, DateTimeOffset now, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var ciphertext = protector.ProtectProof(accountId, contactId, id, token);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.notification_email_deliveries
                (id, account_id, proof_contact_id, protected_proof, due_at, state, attempts, next_attempt_at)
            VALUES ({id}, {accountId}, {contactId}, {ciphertext}, {now}, 'Pending', 0, '-infinity')
            """, ct);
    }

    private Task<ContactRow?> ReadContactForAccountAsync(Guid accountId, Guid contactId, CancellationToken ct) =>
        db.Database.SqlQuery<ContactRow>($"""
            SELECT id, account_id, merchant_user_id, destination_hash, destination_hint, locale,
                approved_at, verification_token_hash, verification_expires_at, verified_at,
                opted_in_at, revoked_at, revision
            FROM public.billing_notice_contacts
            WHERE account_id = {accountId} AND id = {contactId}
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

    private Task<OperationRow?> ReadOperationAsync(
        Guid accountId,
        string action,
        Guid operationKey,
        CancellationToken ct) =>
        db.Database.SqlQuery<OperationRow>($"""
            SELECT contact_id, actor_id, payload_hash, resulting_revision
            FROM public.billing_notice_contact_operations
            WHERE account_id = {accountId}
              AND action = {action}
              AND operation_key = {operationKey}
            """).SingleOrDefaultAsync(ct);

    private Task<int> RecordOperationAsync(
        Guid accountId,
        string action,
        Guid operationKey,
        Guid contactId,
        Guid? actorId,
        byte[] payloadHash,
        int resultingRevision,
        DateTimeOffset recordedAt,
        CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.billing_notice_contact_operations
                (account_id, action, operation_key, contact_id, actor_id, payload_hash, resulting_revision, recorded_at)
            VALUES (
                {accountId}, {action}, {operationKey}, {contactId}, {actorId},
                {payloadHash}, {resultingRevision}, {recordedAt})
            """, ct);

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

    private static byte[] HashOperationPayload(
        string action,
        Guid? actorId,
        Guid accountId,
        Guid contactId,
        Guid? merchantUserId,
        string? normalizedEmail,
        bool? optedIn,
        string? normalizedLocaleToken,
        string? proofDigestHex)
    {
        var raw = string.Join(
            '|',
            action,
            actorId?.ToString("D") ?? string.Empty,
            accountId.ToString("D"),
            contactId.ToString("D"),
            merchantUserId?.ToString("D") ?? string.Empty,
            normalizedEmail ?? string.Empty,
            optedIn is null ? string.Empty : (optedIn.Value ? "1" : "0"),
            normalizedLocaleToken ?? string.Empty,
            proofDigestHex ?? string.Empty);
        return SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    }

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

    private sealed record OperationRow(
        Guid ContactId,
        Guid? ActorId,
        byte[] PayloadHash,
        int ResultingRevision);
}
