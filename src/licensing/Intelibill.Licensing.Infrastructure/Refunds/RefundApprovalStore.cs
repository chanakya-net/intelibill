using System.Text.Json;
using Intelibill.Licensing.Application.Refunds;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Status;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Refunds;

/// <summary>
/// SQL-owned refund approval evidence. Runs inside the provider admin scope transaction under forced RLS: the amount
/// row is private finance evidence, the access decision row holds only operational facts. The database guards repeat
/// the receipt lock, the revision and the cumulative ceiling, so a writer that skips this store cannot exceed them.
/// </summary>
internal sealed class RefundApprovalStore(LicensingDbContext db, SubscriptionAccessFactsReader reader) : IRefundApprovalStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<RefundApprovalSource?> SourceAsync(Guid account, Guid request, CancellationToken ct) =>
        db.Database.SqlQuery<RefundApprovalSource>($"""
            SELECT r.account_id, r.refund_request_id, r.order_id, r.confirmation_id, r.attempt_id, r.manual_receipt_id,
                r.payment_amount_paise, r.amount_paise AS requested_amount_paise, r.currency, r.provider_id,
                r.provider_account_id, r.environment, r.configuration_version,
                coalesce(c.facts->'terms'->>'refundPolicyVersion', {RefundApprovalRules.UnrecordedPolicy}) AS refund_policy_version,
                EXISTS (SELECT FROM public.refund_access_decisions d
                    WHERE d.account_id = r.account_id AND d.refund_request_id = r.refund_request_id) AS decided
            FROM public.refund_requests r
            JOIN public.payment_confirmations c ON c.confirmation_id = r.confirmation_id
                AND c.account_id = r.account_id AND c.order_id = r.order_id
            WHERE r.account_id = {account} AND r.refund_request_id = {request}
            """).SingleOrDefaultAsync(ct);

    public Task LockReceiptAsync(Guid confirmation, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT public.lock_refund_receipt({confirmation})", ct);

    public Task<RefundReceiptLedger> LedgerAsync(Guid account, Guid confirmation, CancellationToken ct) =>
        db.Database.SqlQuery<RefundReceiptLedger>($"""
            SELECT coalesce(max(a.receipt_revision), 0)::bigint AS revision,
                public.refund_receipt_occupied_paise({account}, {confirmation}) AS occupied_paise
            FROM public.refund_approvals a WHERE a.account_id = {account} AND a.confirmation_id = {confirmation}
            """).SingleAsync(ct);

    public async Task<RefundAccessTarget?> TargetAsync(RefundApprovalSource source, DateTimeOffset at, CancellationToken ct)
    {
        var account = source.AccountId;
        var funded = await db.Database.SqlQuery<Guid?>($"""
            SELECT coalesce(
                (SELECT f.term_id FROM public.payment_fulfilments f
                    WHERE f.account_id = {account} AND f.order_id = {source.OrderId}
                        AND (f.attempt_id = {source.AttemptId} OR f.manual_receipt_id = {source.ManualReceiptId})),
                (SELECT i.term_id FROM public.capacity_increases i
                    WHERE i.account_id = {account} AND i.order_id = {source.OrderId} AND i.state = 'Applied'
                        AND (i.attempt_id = {source.AttemptId} OR i.receipt_id = {source.ManualReceiptId}))) AS "Value"
            """).SingleAsync(ct);
        if (funded is not { } term || await reader.ReadTermAsync(account, term, ct) is not { } row) return null;

        var shops = await db.Database.SqlQuery<RefundTargetShop>($"""
            SELECT * FROM public.refund_target_shops({account}, {term}, {at}) ORDER BY shop_id
            """).ToArrayAsync(ct);
        var state = await db.Database.SqlQuery<TargetState>($"""
            SELECT coalesce((SELECT max(d.term_revision) FROM public.refund_access_decisions d
                    WHERE d.account_id = {account} AND d.term_id = {term}), 0)::bigint AS term_revision,
                public.refund_term_committed({account}, {term}) AS committed,
                coalesce((SELECT max(i.target_revision) FROM public.capacity_increases i
                    WHERE i.account_id = {account} AND i.term_id = {term} AND i.state = 'Applied'), 0)::bigint AS capacity_revision
            """).SingleAsync(ct);
        return new(row, shops, state.TermRevision, state.Committed, state.CapacityRevision);
    }

    public async Task<StoredRefundApproval?> FindAsync(Guid account, Guid operationKey, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<ReceiptRow>($"""
            SELECT a.approval_id, a.reservation_id, d.decision_id, a.account_id, a.refund_request_id, a.order_id,
                a.confirmation_id, a.operation_key, a.amount_paise, a.requested_amount_paise, a.payment_amount_paise,
                a.remaining_before_paise, a.remaining_after_paise, a.currency, a.receipt_revision, a.admin_id,
                a.mfa_verified_at, a.decided_at, a.reason, a.payload_hash, d.treatment, d.term_id, d.shops::text AS shops,
                d.effective_at, d.previous_paid_through, d.previous_access_ends_at, d.new_paid_through, d.new_access_ends_at
            FROM public.refund_approvals a
            JOIN public.refund_access_decisions d USING (account_id, approval_id)
            WHERE a.account_id = {account} AND a.operation_key = {operationKey}
            """).SingleOrDefaultAsync(ct);
        return row is null ? null : new(row.ToReceipt(), row.PayloadHash);
    }

    public async Task SaveAsync(NewRefundApproval approval, CancellationToken ct)
    {
        var (source, receipt, access) = (approval.Source, approval.Receipt, approval.Receipt.Access);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.refund_approvals (account_id, approval_id, reservation_id, refund_request_id, order_id,
                confirmation_id, attempt_id, manual_receipt_id, operation_key, payload_hash, impact_digest, admin_id,
                session_id, mfa_verified_at, authorized_at, decided_at, provider_id, provider_account_id, environment,
                configuration_version, refund_policy_version, payment_amount_paise, requested_amount_paise, amount_paise,
                remaining_before_paise, remaining_after_paise, receipt_revision, currency, reason)
            VALUES ({receipt.AccountId}, {receipt.ApprovalId}, {receipt.ReservationId}, {receipt.RefundRequestId},
                {receipt.OrderId}, {receipt.ConfirmationId}, {source.AttemptId}, {source.ManualReceiptId},
                {receipt.OperationKey}, {approval.PayloadHash}, {approval.ImpactDigest}, {receipt.AdminId},
                {approval.SessionId}, {receipt.MfaVerifiedAt}, {approval.AuthorizedAt}, {receipt.DecidedAt},
                {source.ProviderId}, {source.ProviderAccountId}, {source.Environment}, {source.ConfigurationVersion},
                {source.RefundPolicyVersion}, {receipt.VerifiedAmountPaise}, {receipt.RequestedAmountPaise},
                {receipt.ApprovedAmountPaise}, {receipt.RemainingBeforePaise}, {receipt.RemainingAfterPaise},
                {receipt.ReceiptVersion}, {receipt.Currency}, {receipt.Reason})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.refund_access_decisions (account_id, decision_id, approval_id, refund_request_id, treatment,
                term_id, term_revision, effective_at, previous_paid_through, previous_access_ends_at, new_paid_through,
                new_access_ends_at, shop_count, shops, decided_at)
            VALUES ({receipt.AccountId}, {receipt.AccessDecisionId}, {receipt.ApprovalId}, {receipt.RefundRequestId},
                {access.Treatment}, {access.TermId}, {approval.TermRevision}, {access.EffectiveAt},
                {access.PreviousPaidThrough}, {access.PreviousAccessEndsAt}, {access.NewPaidThrough},
                {access.NewAccessEndsAt}, {access.Shops.Length}, {JsonSerializer.Serialize(access.Shops, Json)}::jsonb,
                {receipt.DecidedAt})
            """, ct);
    }

    private sealed record TargetState(long TermRevision, bool Committed, long CapacityRevision);

    private sealed record ReceiptRow(Guid ApprovalId, Guid ReservationId, Guid DecisionId, Guid AccountId,
        Guid RefundRequestId, Guid OrderId, Guid ConfirmationId, Guid OperationKey, long AmountPaise,
        long RequestedAmountPaise, long PaymentAmountPaise, long RemainingBeforePaise, long RemainingAfterPaise,
        string Currency, long ReceiptRevision, Guid AdminId, DateTimeOffset MfaVerifiedAt, DateTimeOffset DecidedAt,
        string Reason, string PayloadHash, string Treatment, Guid? TermId, string Shops, DateTimeOffset? EffectiveAt,
        DateTimeOffset? PreviousPaidThrough, DateTimeOffset? PreviousAccessEndsAt, DateTimeOffset? NewPaidThrough,
        DateTimeOffset? NewAccessEndsAt)
    {
        internal RefundApprovalReceipt ToReceipt() => new(ApprovalId, ReservationId, DecisionId, AccountId,
            RefundRequestId, OrderId, ConfirmationId, OperationKey, RefundRequestsV1.Pending, AmountPaise,
            RequestedAmountPaise, PaymentAmountPaise, RemainingBeforePaise, RemainingAfterPaise, Currency,
            ReceiptRevision, AdminId, MfaVerifiedAt.ToUniversalTime(), DecidedAt.ToUniversalTime(), Reason,
            new(Treatment, TermId, JsonSerializer.Deserialize<RefundAccessShop[]>(Shops, Json)!,
                EffectiveAt?.ToUniversalTime(), PreviousPaidThrough?.ToUniversalTime(),
                PreviousAccessEndsAt?.ToUniversalTime(), NewPaidThrough?.ToUniversalTime(),
                NewAccessEndsAt?.ToUniversalTime()));
    }
}
