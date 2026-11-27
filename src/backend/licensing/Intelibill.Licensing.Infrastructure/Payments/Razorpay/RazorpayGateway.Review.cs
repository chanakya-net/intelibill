using Intelibill.Licensing.Application.Payments;

namespace Intelibill.Licensing.Infrastructure.Payments.Razorpay;

internal sealed partial class RazorpayGateway
{
    public async Task<GatewayResult<IReadOnlyList<PaymentObservation>>> InspectCapturesAsync(string orderReference, string? paymentReference,
        CancellationToken ct)
    {
        var collection = await ReadOrderPaymentsAsync(orderReference, ct);
        if (collection is not { Outcome: GatewayOutcome.Succeeded, Value: { } entries })
            return new(collection.Outcome, null);

        var captures = new List<PaymentObservation>();
        foreach (var entry in entries.Where(p => (paymentReference is null || p.Id == paymentReference) && RazorpayWire.AvailableCapture(p)))
        {
            var independent = await ReadPaymentAsync(entry.Id, ct);
            if (independent.Failure == GatewayOutcome.Unavailable)
                return GatewayResult.Unavailable<IReadOnlyList<PaymentObservation>>();
            if (independent.Payment is { } payment && RazorpayWire.SameAvailableCapture(payment, entry))
                captures.Add(RazorpayWire.Observe(Pin, payment));
        }
        return GatewayResult.Success<IReadOnlyList<PaymentObservation>>(captures);
    }
}
