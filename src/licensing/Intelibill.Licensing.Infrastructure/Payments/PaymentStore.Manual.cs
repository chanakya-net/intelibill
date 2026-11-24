using System.Security.Cryptography;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Intelibill.Licensing.Infrastructure.Billing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class PaymentStore
{
    public async Task<ErrorOr<T>> InManualScopeAsync<T>(AdminSessionCaller caller, Guid account, Guid order,
        bool recentMfa, bool lockAccount, Func<DateTimeOffset, CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", ct);
            var actor = (await db.Set<AdminIdentity>().FromSql($"""
                SELECT *, xmin FROM public.admin_identities WHERE id = {caller.AdminId} FOR SHARE
                """).AsNoTracking().ToListAsync(ct)).SingleOrDefault();
            var session = await AdminAuthorityLocks.LockSessionAsync(db, caller, ct);
            if (ManualAuthority(caller, actor, session, recentMfa) is { } denied) return denied;

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT set_config('app.billing_account_id', {account.ToString("D")}, true),
                    set_config('app.manual_admin_id', {caller.AdminId.ToString("D")}, true),
                    set_config('app.manual_session_id', {caller.SessionId.ToString("D")}, true),
                    set_config('app.manual_order_id', {order.ToString("D")}, true)
                """, ct);
            if (lockAccount) await BillingAccountLock.TakeAsync(db, account, ct);
            if (ManualAuthority(caller, actor, session, recentMfa) is { } expired) return expired;
            var result = await operation(session!.MfaVerifiedAt, ct);
            if (result.IsError) return result;
            if (ManualAuthority(caller, actor, session, recentMfa) is { } lost) return lost;
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (PostgresException error) when (error.ConstraintName == "manual_receiving_reference")
        {
            return ManualPaymentErrors.ReferenceConflict;
        }
        catch (Exception error) when (SqlState(error) is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected)
        {
            return CheckoutPaymentErrors.Busy;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or CryptographicException
            or DbUpdateException { InnerException: NpgsqlException })
        {
            ct.ThrowIfCancellationRequested();
            LogUnavailable(logger, SqlState(error) ?? error.GetType().Name);
            return ManualPaymentErrors.Unavailable;
        }
    }

    private Error? ManualAuthority(AdminSessionCaller caller, AdminIdentity? actor, AdminSession? session, bool recentMfa)
    {
        var now = sessionClock.GetUtcNow();
        if (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now)
            || actor is not { DeactivatedAt: null, MfaEnrolledAt: not null })
            return AdminRefreshErrors.SessionEnded;
        if (!actor.Capabilities.HasFlag(AdminCapability.Billing)) return ManualPaymentErrors.Denied;
        if (recentMfa && !new ProviderActor(caller.AdminId, session.MfaVerifiedAt).HasRecentMfa(now))
            return ManualPaymentErrors.RecentMfa;
        return null;
    }

    public Task<DateTimeOffset?> ManualVerifiedAtAsync(Guid account, Guid order, CancellationToken ct) =>
        db.Database.SqlQuery<DateTimeOffset?>($"""
            SELECT min(verified_at) AS "Value" FROM public.manual_payment_receipts
            WHERE account_id = {account} AND order_id = {order}
            """).SingleAsync(ct);

    public Task<bool> OnlineFundsNeedReviewAsync(Guid account, Guid order, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.payment_events e WHERE e.account_id = {account} AND e.order_id = {order}
                AND e.kind = {PaymentEventKinds.PaidNeedsReview} AND NOT EXISTS (
                    SELECT FROM public.payment_restoration_holds h JOIN public.payment_fulfilments f USING (account_id, order_id)
                    WHERE h.account_id = e.account_id AND h.order_id = e.order_id AND h.attempt_id = e.attempt_id
                        AND f.attempt_id = e.attempt_id)) AS "Value"
            """).SingleAsync(ct);

    public async Task<StoredManualReceipt?> FindManualAsync(Guid account, Guid key, bool byOperation, CancellationToken ct)
    {
        var rows = ManualReceiptRows(account, includeEvidence: true);
        var row = await (byOperation ? rows.Where(r => r.OperationKey == key) : rows.Where(r => r.ReceiptId == key))
            .SingleOrDefaultAsync(ct);
        return row is null ? null : new(row.OrderId, row.ToReceipt(), row.ProtectedPayload);
    }

    public async Task<IReadOnlyList<ManualPaymentReceipt>> ListManualAsync(Guid account, Guid order, CancellationToken ct)
    {
        var rows = await ManualReceiptRows(account, includeEvidence: false).Where(r => r.OrderId == order)
            .OrderByDescending(r => r.VerifiedAt).ThenBy(r => r.ReceiptId).Take(40).ToListAsync(ct);
        return rows.Select(row => row.ToReceipt()).ToArray();
    }

    private IQueryable<ManualReceiptRow> ManualReceiptRows(Guid account, bool includeEvidence) =>
        db.Database.SqlQuery<ManualReceiptRow>($"""
            SELECT r.receipt_id,r.order_id,r.operation_key,r.admin_id,r.mfa_verified_at,r.received_at,r.verified_at,
                r.amount_paise,r.currency,r.receiving_account_id,r.configuration_version,r.evidence_policy_version,r.environment,
                CASE WHEN i.state = 'Applied' OR sr.state = 'Applied' THEN NULL
                    WHEN i.state = 'NeedsReview' THEN i.reason
                    WHEN sr.state IN ('NeedsResolution', 'NeedsReview') THEN sr.reason
                    ELSE r.review_reason END AS review_reason,
                CASE WHEN i.state = 'Applied' THEN i.term_id
                    WHEN sr.state = 'Applied' THEN sr.successor_term_id
                    ELSE r.term_id END AS term_id,
                CASE WHEN {includeEvidence} THEN r.protected_payload ELSE '' END AS protected_payload
            FROM public.manual_payment_receipts r
            LEFT JOIN public.capacity_increases i
                ON i.account_id = r.account_id AND i.order_id = r.order_id AND i.receipt_id = r.receipt_id
            LEFT JOIN public.scheduled_capacity_reductions sr
                ON sr.account_id = r.account_id AND sr.order_id = r.order_id AND sr.receipt_id = r.receipt_id
            WHERE r.account_id = {account}
            """);

    public Task UseManualReceiptAsync(Guid receipt, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.manual_receipt_id', {receipt.ToString("D")}, true)
            """, ct);

    public async Task SaveManualAsync(Guid account, Guid order, AdminSessionCaller caller, ConfirmManualPayment command,
        ManualPaymentReceipt receipt, string digest, string protectedPayload, CancellationToken ct)
    {
        await UseManualReceiptAsync(receipt.ReceiptId, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.manual_payment_receipts (receipt_id, account_id, order_id, operation_key, admin_id, session_id,
                mfa_verified_at, received_at, verified_at, amount_paise, currency, receiving_account_id, configuration_version,
                evidence_policy_version, environment, review_reason, term_id, protected_payload, reference_digest, quote_revision,
                snapshot_hash)
            VALUES ({receipt.ReceiptId}, {account}, {order}, {receipt.OperationKey}, {caller.AdminId}, {caller.SessionId},
                {receipt.MfaVerifiedAt}, {receipt.ReceivedAt}, {receipt.VerifiedAt}, {receipt.AmountPaise}, {receipt.Currency},
                {receipt.ReceivingAccountId}, {receipt.ConfigurationVersion}, {receipt.EvidencePolicyVersion}, {receipt.Environment},
                {receipt.ReviewReason}, {receipt.TermId}, {protectedPayload}, {digest}, {command.ExpectedRevision}, {command.SnapshotHash})
            """, ct);
    }

    private sealed record ManualReceiptRow(Guid ReceiptId, Guid OrderId, Guid OperationKey, Guid AdminId, DateTimeOffset MfaVerifiedAt,
        DateTimeOffset ReceivedAt, DateTimeOffset VerifiedAt, long AmountPaise, string Currency, string ReceivingAccountId,
        string ConfigurationVersion, string EvidencePolicyVersion, string Environment, string? ReviewReason, Guid? TermId,
        string ProtectedPayload)
    {
        internal ManualPaymentReceipt ToReceipt() => new(ReceiptId, OperationKey, AdminId, MfaVerifiedAt.ToUniversalTime(),
            ReceivedAt.ToUniversalTime(), VerifiedAt.ToUniversalTime(), AmountPaise, Currency, ReceivingAccountId,
            ConfigurationVersion, EvidencePolicyVersion, Environment, ReviewReason, TermId);
    }
}
