using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;

namespace Intelibill.Licensing.Infrastructure.Payments.Razorpay;

internal sealed partial class RazorpayGateway
{
    public bool SupportsRefund(RefundRequest request) => request.Pin == Pin && request.Payment is { } original &&
        request.RefundId != Guid.Empty && request.Currency == "INR" && original.Currency == request.Currency &&
        request.AmountPaise >= 100 && request.AmountPaise <= original.AmountPaise &&
        request.ProviderOrderReference is { } order && SafeReference(order, "order_") && SafeReference(request.ProviderPaymentReference, "pay_");

    public async Task<GatewayResult<RefundObservation>> RequestRefundAsync(RefundRequest request, CancellationToken ct)
    {
        if (!SupportsRefund(request)) return GatewayResult.Unsupported<RefundObservation>();
        if (!await OriginalCaptureAsync(request, ct)) return GatewayResult.Rejected<RefundObservation>();
        using var post = new HttpRequestMessage(HttpMethod.Post, $"v1/payments/{request.ProviderPaymentReference}/refund")
        {
            Content = JsonContent.Create(new { amount = request.AmountPaise, speed = "normal", receipt = request.Receipt }),
        };
        var read = await SendAsync(post, ct);
        return read.Failure is { } failure ? new(failure, null) : ParseRefund(request, read.Root, null);
    }

    public async Task<GatewayResult<RefundObservation>> GetRefundAsync(RefundRequest request, string? reference, CancellationToken ct)
    {
        if (!SupportsRefund(request)) return GatewayResult.Unsupported<RefundObservation>();
        if (!await OriginalCaptureAsync(request, ct)) return GatewayResult.Rejected<RefundObservation>();
        var path = $"v1/payments/{request.ProviderPaymentReference}/refunds";
        reference ??= await FindRefundAsync(request, path, ct);
        if (reference is null || !SafeReference(reference, "rfnd_")) return new(GatewayOutcome.NotFound, null);
        using var get = new HttpRequestMessage(HttpMethod.Get, $"{path}/{reference}");
        var read = await SendAsync(get, ct);
        return read.Failure is { } failure ? new(failure, null) : ParseRefund(request, read.Root, reference);
    }

    private async Task<bool> OriginalCaptureAsync(RefundRequest request, CancellationToken ct)
    {
        var original = request.Payment!;
        var order = await ReadOrderAsync(new(request.ProviderOrderReference!, original.AmountPaise, original.Currency, original), ct);
        if (order.Order is null || !RazorpayWire.PaidInFull(order.Order)) return false;
        var read = await ReadPaymentAsync(request.ProviderPaymentReference, ct);
        return read.Payment is { Captured: true, Status: "captured" or "refunded" } payment &&
            payment.OrderId == order.Order.Id && payment.Amount == original.AmountPaise && payment.Currency == original.Currency &&
            (payment.AmountCaptured ?? payment.Amount) == payment.Amount && config.Methods.Contains(payment.Method ?? "");
    }

    private async Task<string?> FindRefundAsync(RefundRequest request, string path, CancellationToken ct)
    {
        const int size = 10;
        string? match = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var skip = 0; skip < 100; skip += size)
        {
            using var get = new HttpRequestMessage(HttpMethod.Get, $"{path}?count={size}&skip={skip}");
            var read = await SendAsync(get, ct);
            if (read.Failure is not null || RazorpayWire.Items(read.Root) is not { Count: <= size } items) return null;
            foreach (var row in items)
            {
                if (Text(row, "entity") != "refund" || Text(row, "payment_id") != request.ProviderPaymentReference ||
                    Text(row, "id") is not { } id || !SafeReference(id, "rfnd_") || !seen.Add(id)) return null;
                if (Text(row, "receipt") != request.Receipt) continue;
                if (match is not null) return null;
                match = id;
            }
            if (items.Count < size) return match;
        }
        return null;
    }

    private GatewayResult<RefundObservation> ParseRefund(RefundRequest request, JsonElement row, string? reference)
    {
        var state = Text(row, "status") switch
        {
            "processed" => ObservedRefundState.Processed,
            "failed" => ObservedRefundState.Failed,
            "pending" => ObservedRefundState.Pending,
            _ => (ObservedRefundState?)null,
        };
        if (state is null || Text(row, "entity") != "refund" || Text(row, "id") is not { } id ||
            !SafeReference(id, "rfnd_") || (reference is not null && reference != id) ||
            Text(row, "receipt") != request.Receipt || Text(row, "payment_id") != request.ProviderPaymentReference ||
            Text(row, "currency") != request.Currency || Property(row, "amount") is not { ValueKind: JsonValueKind.Number } amount ||
            !amount.TryGetInt64(out var paise) || paise != request.AmountPaise) return GatewayResult.Rejected<RefundObservation>();
        return GatewayResult.Success(new RefundObservation(Pin, id, state.Value, paise, request.Currency,
            request.ProviderPaymentReference, request.Receipt));
    }
}
