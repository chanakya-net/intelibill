using Intelibill.Licensing.Application.Entitlements;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed class RefundAttemptDispatcher(LicensingDbContext db, IPaymentGatewayResolver gateways,
    IEntitlementPublisher publisher, [FromKeyedServices(PendingPaymentAttemptWorker.ClockKey)] TimeProvider clock) : IRefundReconciliation
{
    public async Task<bool> RunOneAsync(CancellationToken ct)
    {
        var token = Guid.NewGuid();
        var lease = await db.Database.SqlQuery<Lease>($"""
            SELECT * FROM public.claim_refund({clock.GetUtcNow()},{token})
            """).SingleOrDefaultAsync(ct);
        if (lease is null) return false;
        var request = lease.ToRequest();
        var gateway = gateways.ForPin(request.Pin);
        var observation = new Result(lease.Sent ? "Unknown" : "Reserved", "OriginalAdapterUnavailable");
        if (gateway?.Pin == request.Pin && gateway.SupportsRefund(request))
            observation = await ObserveAsync(gateway, request, lease, token, ct);
        await FinishAsync(token, lease, observation, ct);
        return true;
    }

    private async Task<Result> ObserveAsync(IPaymentGateway gateway, RefundRequest request, Lease lease,
        Guid token, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(25));
        var reference = lease.RefundReference;
        try
        {
            if (!lease.Sent)
            {
                // No enclosing transaction. Only a positively acknowledged, committed first send may reach the adapter.
                if (!await db.Database.SqlQuery<bool>($"""
                    SELECT public.start_refund_send({token},{clock.GetUtcNow()}) AS "Value"
                    """).SingleAsync(ct)) return new("Unknown", "SendAlreadyClaimed");
                var post = await gateway.RequestRefundAsync(request, budget.Token);
                if (!Matches(post, request, null)) return new("Unknown", "DispatchUncertain");
                reference = post.Value!.ProviderRefundReference;
            }
            var read = await gateway.GetRefundAsync(request, reference, budget.Token);
            if (!Matches(read, request, reference)) return new("Unknown", "LookupUnproven", reference);
            var value = read.Value!;
            return new(value.State switch
            {
                ObservedRefundState.Processed => "Confirmed",
                ObservedRefundState.Failed => "Failed",
                _ => "Pending",
            }, "OriginalProviderRead", value.ProviderRefundReference);
        }
        catch (Exception error) when (!ct.IsCancellationRequested && error is HttpRequestException or IOException or OperationCanceledException)
        {
            return new("Unknown", "TransportUnavailable", reference);
        }
    }

    private static bool Matches(GatewayResult<RefundObservation> result, RefundRequest request, string? reference) =>
        result is { Outcome: GatewayOutcome.Succeeded, Value: { } value } && value.Pin == request.Pin &&
        value.Currency == request.Currency && value.AmountPaise == request.AmountPaise &&
        value.Receipt == request.Receipt && value.ProviderPaymentReference == request.ProviderPaymentReference &&
        value.ProviderRefundReference is { Length: > 0 and <= 120 } && (reference is null || reference == value.ProviderRefundReference);

    private async Task FinishAsync(Guid token, Lease lease, Result result, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var shops = await db.Database.SqlQuery<Guid>($"""
            SELECT public.finish_refund({token},{now},{result.State},{result.Reason},{result.Reference}) AS "Value"
            """).ToArrayAsync(ct);
        foreach (var shop in shops) await publisher.PublishAsync(lease.AccountId, shop, ct, now);
        await tx.CommitAsync(ct);
    }

    private sealed record Result(string State, string Reason, string? Reference = null);
    private sealed record Lease(Guid ReservationId, Guid AccountId, Guid OrderId, Guid AttemptId, string ProviderId,
        string ProviderAccountId, string Environment, string ConfigurationVersion, string PaymentReference, string OrderReference,
        long AmountPaise, long PaymentAmountPaise, string Currency, bool Sent, string? RefundReference)
    {
        internal RefundRequest ToRequest() => new(new(ProviderId, ProviderAccountId, Environment, ConfigurationVersion),
            PaymentReference, AmountPaise, Currency, ReservationId, OrderReference, new(AccountId, OrderId, AttemptId, PaymentAmountPaise, Currency));
    }
}
