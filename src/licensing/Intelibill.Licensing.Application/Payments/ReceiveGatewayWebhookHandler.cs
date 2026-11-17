using System.Security.Cryptography;

namespace Intelibill.Licensing.Application.Payments;

public enum GatewayWebhookReceipt { Disabled, Rejected, Stored, Duplicate }

public sealed class ReceiveGatewayWebhookHandler(IPaymentGatewayResolver gateways, IGatewayInboxStore store,
    IGatewayInboxEvidence evidence, TimeProvider clock)
{
    public async Task<GatewayWebhookReceipt> HandleAsync(string provider, ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var candidates = gateways.ForWebhooks(provider);
        if (candidates.Count == 0)
            return GatewayWebhookReceipt.Disabled;

        foreach (var gateway in candidates)
        {
            if (gateway.ValidateWebhook(body.Span, headers) is not { Outcome: GatewayOutcome.Succeeded, Value: { } parsed } ||
                parsed.Pin != gateway.Pin)
                continue;
            var inserted = await store.ReceiveAsync(parsed, SHA256.HashData(body.Span), evidence.Protect(parsed),
                clock.GetUtcNow(), ct);
            return inserted ? GatewayWebhookReceipt.Stored : GatewayWebhookReceipt.Duplicate;
        }
        return GatewayWebhookReceipt.Rejected;
    }
}
