using Intelibill.Licensing.Application.Payments;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments.Development;

internal sealed partial class DevelopmentPaymentGateway
{
    public async Task<GatewayOutcome> PreflightRefundAsync(RefundRequest request, CancellationToken ct)
    {
        if (!((IPaymentGateway)this).SupportsRefund(request)) return GatewayOutcome.Unsupported;
        var captured = await InGatewayAsync(token => db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM public.development_gateway_payments p
                JOIN public.development_gateway_sessions s USING(provider_order_reference)
                WHERE p.provider_payment_reference={request.ProviderPaymentReference} AND p.provider_order_reference={request.ProviderOrderReference}
                    AND s.account_id={request.Payment!.AccountId} AND s.attempt_id={request.Payment.AttemptId} AND s.order_id={request.Payment.OrderId}
                    AND p.state='Captured' AND p.currency={request.Currency} AND p.amount_paise={request.Payment.AmountPaise}) AS "Value"
            """).SingleAsync(token), ct);
        return captured ? GatewayOutcome.Succeeded : GatewayOutcome.Rejected;
    }

    public async Task<GatewayResult<RefundObservation>> RequestRefundAsync(RefundRequest request, CancellationToken ct)
    {
        if (!((IPaymentGateway)this).SupportsRefund(request)) return GatewayResult.Unsupported<RefundObservation>();
        var id = "dev_refund_" + Guid.NewGuid().ToString("N");
        var inserted = await InGatewayAsync(token => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.development_gateway_refunds
                (reference,receipt,payment_reference,amount_paise,currency,provider_account_id,environment,configuration_version)
            SELECT {id},{request.Receipt},p.provider_payment_reference,{request.AmountPaise},p.currency,
                s.provider_account_id,s.environment,s.configuration_version FROM public.development_gateway_payments p
            JOIN public.development_gateway_sessions s USING(provider_order_reference)
            WHERE p.provider_payment_reference={request.ProviderPaymentReference} AND p.provider_order_reference={request.ProviderOrderReference}
                AND s.account_id={request.Payment!.AccountId} AND s.attempt_id={request.Payment.AttemptId} AND s.order_id={request.Payment.OrderId}
                AND p.state='Captured' AND p.currency={request.Currency} AND p.amount_paise={request.Payment.AmountPaise}
            """, token), ct);
        if (inserted != 1) return GatewayResult.Rejected<RefundObservation>();
        var observed = await ReadRefundAsync(request, id, ct);
        return observed is { Control: "Timeout" } ? GatewayResult.Unavailable<RefundObservation>() : ObserveRefund(request, observed);
    }

    public async Task<GatewayResult<RefundObservation>> GetRefundAsync(RefundRequest request, string? reference, CancellationToken ct)
    {
        if (!((IPaymentGateway)this).SupportsRefund(request)) return GatewayResult.Unsupported<RefundObservation>();
        return ObserveRefund(request, await ReadRefundAsync(request, reference, ct));
    }

    private Task<RefundRow?> ReadRefundAsync(RefundRequest request, string? reference, CancellationToken ct) =>
        InGatewayAsync(token => db.Database.SqlQuery<RefundRow>($"""
            SELECT r.reference,r.receipt,r.payment_reference,r.amount_paise,r.currency,
                coalesce(c.outcome,'Processed') AS control,c.available_at
            FROM public.development_gateway_refunds r
            JOIN public.development_gateway_payments p ON p.provider_payment_reference=r.payment_reference
            JOIN public.development_gateway_sessions s USING(provider_order_reference)
            LEFT JOIN public.development_refund_controls c ON c.receipt=r.receipt
            WHERE r.receipt={request.Receipt} AND r.payment_reference={request.ProviderPaymentReference}
                AND s.provider_order_reference={request.ProviderOrderReference} AND s.attempt_id={request.Payment!.AttemptId}
                AND s.order_id={request.Payment.OrderId} AND s.account_id={request.Payment.AccountId}
                AND p.amount_paise={request.Payment.AmountPaise} AND p.currency={request.Currency}
                AND ({reference}::text IS NULL OR r.reference={reference})
            """).SingleOrDefaultAsync(token), ct);

    private GatewayResult<RefundObservation> ObserveRefund(RefundRequest request, RefundRow? row)
    {
        if (row is null || row.AmountPaise != request.AmountPaise || row.Currency != request.Currency ||
            row.Control == "Mismatch") return GatewayResult.Rejected<RefundObservation>();
        var state = row.Control switch
        {
            "Processed" => ObservedRefundState.Processed,
            "Failed" => ObservedRefundState.Failed,
            "Delayed" when row.AvailableAt <= Now() => ObservedRefundState.Processed,
            _ => ObservedRefundState.Pending,
        };
        return GatewayResult.Success(new RefundObservation(Pin, row.Reference, state, row.AmountPaise, row.Currency, row.PaymentReference, row.Receipt));
    }

    private sealed record RefundRow(string Reference, string Receipt, string PaymentReference, long AmountPaise, string Currency,
        string Control, DateTimeOffset? AvailableAt);
}
