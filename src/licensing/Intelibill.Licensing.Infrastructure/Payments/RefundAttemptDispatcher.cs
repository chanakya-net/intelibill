using Intelibill.Licensing.Application.Entitlements;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Payments.Development;
using Intelibill.Licensing.Infrastructure.Payments.Razorpay;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed class RefundAttemptDispatcher(LicensingDbContext db, IPaymentGatewayResolver gateways,
    IEntitlementPublisher publisher, IHostEnvironment environment,
    [FromKeyedServices(PendingPaymentAttemptWorker.ClockKey)] TimeProvider clock) : IRefundReconciliation
{
    public async Task<bool> RunOneAsync(CancellationToken ct)
    {
        var token = Guid.NewGuid();
        var lease = await db.Database.SqlQuery<Lease>($"""
            SELECT * FROM public.claim_refund({Now()},{token})
            """).SingleOrDefaultAsync(ct);
        if (lease is null) return false;
        var observation = await EvaluateAsync(lease, token, ct);
        await FinishAsync(token, lease, observation, ct);
        return true;
    }

    private async Task<Result> EvaluateAsync(Lease lease, Guid token, CancellationToken ct)
    {
        var request = lease.ToRequest();
        if (!lease.Sent && !CanDispatchRefund(request.Pin, environment))
            return new("Reserved", "LiveDispatchDisabled");
        var gateway = gateways.ForPin(request.Pin);
        if (gateway is null || gateway.Pin != request.Pin)
            return new(lease.Sent ? "Unknown" : "Reserved", "OriginalAdapterUnavailable");
        if (!gateway.SupportsRefund(request))
            return new(lease.Sent ? "Unknown" : "Reserved", UnsupportedReason(gateway, request));
        return await ObserveAsync(gateway, request, lease, token, ct);
    }

    internal static bool CanDispatchRefund(PaymentProviderPin pin, IHostEnvironment host) =>
        (host.IsDevelopment() || host.IsEnvironment("Testing")) &&
        ((pin.ProviderId == DevelopmentPaymentGateway.ProviderId && pin.Environment == "Development") ||
         (pin.ProviderId == RazorpayConfiguration.ProviderId && pin.Environment == "Test"));

    private static string UnsupportedReason(IPaymentGateway gateway, RefundRequest request) =>
        gateway.Capabilities.Refunds && gateway.MinimumAmountPaise(request.Currency) is { } min && request.AmountPaise < min
            ? "BelowProviderMinimum" : "ProviderRefundUnsupported";

    private async Task<Result> ObserveAsync(IPaymentGateway gateway, RefundRequest request, Lease lease,
        Guid token, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(25));
        var sendStarted = lease.Sent;
        var bound = lease.RefundReference;
        var candidate = bound is null ? lease.CandidateReference : null;
        try
        {
            if (!sendStarted)
            {
                var preflight = await gateway.PreflightRefundAsync(request, budget.Token);
                if (preflight != GatewayOutcome.Succeeded)
                    return new("Reserved", preflight == GatewayOutcome.Unavailable ? "PreflightUnavailable" : "OriginalCaptureUnverified");
                // No enclosing transaction. Revalidate the lease and commit the only send right immediately before the POST.
                if (!await db.Database.SqlQuery<bool>($"""
                    SELECT public.start_refund_send({token},{Now()}) AS "Value"
                    """).SingleAsync(ct)) return new("Unknown", "SendAlreadyClaimed");
                sendStarted = true;
                var post = await gateway.SendRefundAsync(request, budget.Token);
                if (!Matches(post, request, null)) return new("Unknown", "DispatchUncertain");
                candidate = post.Value!.ProviderRefundReference;
            }
            return await ReconcileReadAsync(gateway, request, bound, candidate, budget.Token);
        }
        catch (Exception error) when (!ct.IsCancellationRequested && error is HttpRequestException or IOException or OperationCanceledException)
        {
            return sendStarted ? new("Unknown", "TransportUnavailable", bound ?? candidate) : new("Reserved", "PreflightUnavailable");
        }
    }

    private static async Task<Result> ReconcileReadAsync(IPaymentGateway gateway, RefundRequest request,
        string? bound, string? candidate, CancellationToken ct)
    {
        if (bound is not null)
        {
            var read = await gateway.GetRefundAsync(request, bound, ct);
            return Matches(read, request, bound) ? Observed(read.Value!)
                : new("Unknown", read.Outcome == GatewayOutcome.Unavailable ? "LookupUnavailable" : "LookupUnproven", bound);
        }
        if (candidate is not null)
        {
            var direct = await gateway.GetRefundAsync(request, candidate, ct);
            if (Matches(direct, request, candidate)) return Observed(direct.Value!);
            if (direct.Outcome == GatewayOutcome.Unavailable) return new("Unknown", "LookupUnavailable", candidate);
            var discovered = await gateway.GetRefundAsync(request, null, ct);
            if (Matches(discovered, request, null)) return Observed(discovered.Value!);
            return new("Unknown", direct is RefundReceiptMismatch ? "CandidateMismatch" : "LookupUnproven", candidate);
        }
        var listed = await gateway.GetRefundAsync(request, null, ct);
        return Matches(listed, request, null) ? Observed(listed.Value!)
            : new("Unknown", listed.Outcome == GatewayOutcome.Unavailable ? "LookupUnavailable" : "LookupUnproven");
    }

    private static Result Observed(RefundObservation value) => new(value.State switch
    {
        ObservedRefundState.Processed => "Confirmed",
        ObservedRefundState.Failed => "Failed",
        _ => "Pending",
    }, "OriginalProviderRead", value.ProviderRefundReference);

    private static bool Matches(GatewayResult<RefundObservation> result, RefundRequest request, string? reference) =>
        result is { Outcome: GatewayOutcome.Succeeded, Value: { } value } && value.Pin == request.Pin &&
        value.Currency == request.Currency && value.AmountPaise == request.AmountPaise &&
        value.Receipt == request.Receipt && value.ProviderPaymentReference == request.ProviderPaymentReference &&
        value.ProviderRefundReference is { Length: > 0 and <= 120 } && (reference is null || reference == value.ProviderRefundReference);

    private async Task FinishAsync(Guid token, Lease lease, Result result, CancellationToken ct)
    {
        var now = Now();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var shops = await db.Database.SqlQuery<Guid>($"""
            SELECT public.finish_refund({token},{now},{result.State},{result.Reason},{result.Reference}) AS "Value"
            """).ToArrayAsync(ct);
        foreach (var shop in shops) await publisher.PublishAsync(lease.AccountId, shop, ct, now);
        await tx.CommitAsync(ct);
    }

    private DateTimeOffset Now() => EntitlementSnapshotsV1.Instant(clock);

    private sealed record Result(string State, string Reason, string? Reference = null);
    private sealed record Lease(Guid ReservationId, Guid AccountId, Guid OrderId, Guid AttemptId, string ProviderId,
        string ProviderAccountId, string Environment, string ConfigurationVersion, string PaymentReference, string OrderReference,
        long AmountPaise, long PaymentAmountPaise, string Currency, bool Sent, string? RefundReference, string? CandidateReference)
    {
        internal RefundRequest ToRequest() => new(new(ProviderId, ProviderAccountId, Environment, ConfigurationVersion),
            PaymentReference, AmountPaise, Currency, ReservationId, OrderReference, new(AccountId, OrderId, AttemptId, PaymentAmountPaise, Currency));
    }
}
