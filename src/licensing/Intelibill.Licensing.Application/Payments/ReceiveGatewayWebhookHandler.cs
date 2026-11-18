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

        var authenticated = new List<VerifiedGatewayEvent>();
        foreach (var gateway in candidates)
        {
            if (gateway.ValidateWebhook(body.Span, headers) is { Outcome: GatewayOutcome.Succeeded, Value: { } parsed } &&
                parsed.Pin == gateway.Pin)
                authenticated.Add(parsed);
        }
        // A shared credential cannot identify which receiving account/environment authenticated this delivery.
        if (authenticated.Select(e => (e.Pin.ProviderId, e.Pin.ProviderAccountId, e.Pin.Environment)).Distinct().Count() != 1)
            return GatewayWebhookReceipt.Rejected;
        // Rotation versions are opaque: ordinal order is deterministic, not a claim about chronological recency.
        var received = authenticated.OrderBy(e => e.Pin.ConfigurationVersion, StringComparer.Ordinal).First();
        var inserted = await store.ReceiveAsync(received, SHA256.HashData(body.Span), evidence.Protect(received),
            clock.GetUtcNow(), ct);
        return inserted ? GatewayWebhookReceipt.Stored : GatewayWebhookReceipt.Duplicate;
    }
}
