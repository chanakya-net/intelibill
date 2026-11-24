using Intelibill.Licensing.Application.Refunds;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Refunds;

/// <summary>
/// SQL-owned refund request evidence. Runs inside the delegation or admin scope transaction under forced RLS.
/// Status is derived: <see cref="RefundRequestsV1.Requested"/> until a committed approval's access decision exists,
/// then <see cref="RefundRequestsV1.Pending"/>; there is no mutable status column. The create replay keeps its original response.
/// </summary>
internal sealed class RefundRequestStore(LicensingDbContext db) : IRefundRequestStore
{
    public async Task<StoredRefundRequest?> FindAsync(Guid account, Guid operationKey, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<StoredRow>($"""
            SELECT refund_request_id, account_id, order_id, confirmation_id, amount_paise, payment_amount_paise,
                currency, reason, requested_at, actor_id, request_hash
            FROM public.refund_requests
            WHERE account_id = {account} AND operation_key = {operationKey}
            """).SingleOrDefaultAsync(ct);
        return row is null ? null : new(row.ToContract(), row.ActorId, row.RequestHash);
    }

    public Task<RefundablePayment?> PaymentAsync(Guid account, Guid order, Guid confirmation, CancellationToken ct) =>
        db.Database.SqlQuery<RefundablePayment>($"""
            SELECT confirmation_id, order_id, attempt_id, manual_receipt_id,
                (facts->'receipt'->>'amountPaise')::bigint AS amount_paise,
                facts->'receipt'->>'currency' AS currency,
                facts->>'providerId' AS provider_id,
                facts->>'providerAccountId' AS provider_account_id,
                facts->>'environment' AS environment,
                facts->>'configurationVersion' AS configuration_version
            FROM public.payment_confirmations
            WHERE account_id = {account} AND order_id = {order} AND confirmation_id = {confirmation}
            """).SingleOrDefaultAsync(ct);

    public Task SaveAsync(RefundRequestV1 request, RefundablePayment payment, Guid actor, Guid operationKey,
        string requestHash, Guid replay, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.refund_requests (account_id, refund_request_id, order_id, confirmation_id,
                attempt_id, manual_receipt_id, actor_id, operation_key, request_hash, replay_id,
                provider_id, provider_account_id, environment, configuration_version,
                payment_amount_paise, amount_paise, currency, reason, requested_at)
            VALUES ({request.AccountId}, {request.RefundRequestId}, {request.OrderId}, {request.ConfirmationId},
                {payment.AttemptId}, {payment.ManualReceiptId}, {actor}, {operationKey}, {requestHash}, {replay},
                {payment.ProviderId}, {payment.ProviderAccountId}, {payment.Environment}, {payment.ConfigurationVersion},
                {request.PaymentAmountPaise}, {request.AmountPaise}, {request.Currency}, {request.Reason}, {request.RequestedAt})
            """, ct);

    public async Task<RefundRequestPageV1> PageAsync(Guid account, Guid? after, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<CustomerRow>($"""
            SELECT refund_request_id, account_id, order_id, confirmation_id, CASE WHEN EXISTS (SELECT FROM public.refund_access_decisions d
                    WHERE d.account_id = r.account_id AND d.refund_request_id = r.refund_request_id)
                    THEN 'Pending' ELSE 'Requested' END AS status,
                amount_paise, payment_amount_paise, currency, reason, requested_at
            FROM public.refund_requests r
            WHERE account_id = {account}
                AND ({after}::uuid IS NULL OR (requested_at, refund_request_id) > (
                    SELECT requested_at, refund_request_id FROM public.refund_requests
                    WHERE account_id = {account} AND refund_request_id = {after}))
            ORDER BY requested_at, refund_request_id
            LIMIT {RefundRequestsV1.PageSize + 1}
            """).ToListAsync(ct);
        var items = rows.Take(RefundRequestsV1.PageSize).Select(row => row.ToContract()).ToArray();
        return new(account, items, rows.Count > items.Length ? items[^1].RefundRequestId : null);
    }

    public async Task<RefundIntakePage> IntakeAsync(Guid account, Guid? after, CancellationToken ct, Guid? request = null)
    {
        var rows = await db.Database.SqlQuery<IntakeRow>($"""
            SELECT refund_request_id, account_id, order_id, confirmation_id, actor_id,
                CASE WHEN attempt_id IS NOT NULL THEN 'Online' ELSE 'Manual' END AS payment_source, CASE WHEN EXISTS (SELECT FROM public.refund_access_decisions d
                    WHERE d.account_id = r.account_id AND d.refund_request_id = r.refund_request_id)
                    THEN 'Pending' ELSE 'Requested' END AS status,
                amount_paise, payment_amount_paise, currency, reason, requested_at,
                provider_id, provider_account_id, environment, configuration_version
            FROM public.refund_requests r
            WHERE account_id = {account} AND ({request}::uuid IS NULL OR refund_request_id = {request})
                AND ({after}::uuid IS NULL OR (requested_at, refund_request_id) > (
                    SELECT requested_at, refund_request_id FROM public.refund_requests
                    WHERE account_id = {account} AND refund_request_id = {after}))
            ORDER BY requested_at, refund_request_id
            LIMIT {RefundRequestsV1.PageSize + 1}
            """).ToListAsync(ct);
        var items = rows.Take(RefundRequestsV1.PageSize).Select(row => row.ToItem()).ToArray();
        return new(account, items, rows.Count > items.Length ? items[^1].RefundRequestId : null);
    }

    private sealed record CustomerRow(Guid RefundRequestId, Guid AccountId, Guid OrderId, Guid ConfirmationId,
        string Status, long AmountPaise, long PaymentAmountPaise, string Currency, string Reason, DateTimeOffset RequestedAt)
    {
        internal RefundRequestV1 ToContract() => new(RefundRequestId, AccountId, OrderId, ConfirmationId,
            Status, AmountPaise, PaymentAmountPaise, Currency, Reason, RequestedAt.ToUniversalTime());
    }

    private sealed record StoredRow(Guid RefundRequestId, Guid AccountId, Guid OrderId, Guid ConfirmationId,
        long AmountPaise, long PaymentAmountPaise, string Currency, string Reason, DateTimeOffset RequestedAt,
        Guid ActorId, string RequestHash)
    {
        internal RefundRequestV1 ToContract() => new(RefundRequestId, AccountId, OrderId, ConfirmationId,
            RefundRequestsV1.Requested, AmountPaise, PaymentAmountPaise, Currency, Reason, RequestedAt.ToUniversalTime());
    }

    private sealed record IntakeRow(Guid RefundRequestId, Guid AccountId, Guid OrderId, Guid ConfirmationId,
        Guid ActorId, string PaymentSource, string Status, long AmountPaise, long PaymentAmountPaise, string Currency, string Reason,
        DateTimeOffset RequestedAt, string ProviderId, string ProviderAccountId, string Environment,
        string ConfigurationVersion)
    {
        internal RefundIntakeItem ToItem() => new(RefundRequestId, AccountId, OrderId, ConfirmationId, ActorId,
            PaymentSource, Status, AmountPaise, PaymentAmountPaise, Currency, Reason,
            RequestedAt.ToUniversalTime(), ProviderId, ProviderAccountId, Environment, ConfigurationVersion);
    }
}
