using System.Text.Json;
using Intelibill.Licensing.Application.Payments;

namespace Intelibill.Licensing.Infrastructure.Payments.Razorpay;

internal sealed partial class RazorpayGateway
{
    public GatewayResult<VerifiedGatewayEvent> ValidateWebhook(ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> headers)
    {
        if (config.Webhook is not { } webhook)
            return GatewayResult.Unsupported<VerifiedGatewayEvent>();
        if (!headers.TryGetValue("X-Razorpay-Event-Id", out var id) || !WebhookSignature.Identifier(id, 120) ||
            !headers.TryGetValue("X-Razorpay-Signature", out var signature) ||
            !WebhookSignature.Matches(body, signature, webhook.Secrets))
            return GatewayResult.Rejected<VerifiedGatewayEvent>();

        try
        {
            var reader = new Utf8JsonReader(body, new JsonReaderOptions { MaxDepth = 32 });
            using var json = JsonDocument.ParseValue(ref reader);
            return !reader.Read() && ParseEvent(json.RootElement, id, webhook.AccountId) is { } parsed
                ? GatewayResult.Success(parsed) : GatewayResult.Rejected<VerifiedGatewayEvent>();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return GatewayResult.Rejected<VerifiedGatewayEvent>();
        }
    }

    private VerifiedGatewayEvent? ParseEvent(JsonElement root, string id, string account)
    {
        if (Text(root, "entity") != "event" || Text(root, "account_id") != account ||
            Text(root, "event") is not { Length: > 0 and <= 80 } name)
            return null;
        if (name is "refund.created" or "refund.processed" or "refund.failed" or "refund.speed_changed")
        {
            var entity = Property(Property(Property(root, "payload"), "refund"), "entity");
            var paymentId = Text(entity, "payment_id");
            var refundId = Text(entity, "id");
            var receipt = Text(entity, "receipt");
            if (Text(entity, "entity") != "refund" || !WebhookSignature.Identifier(paymentId, 120) ||
                !WebhookSignature.Identifier(refundId, 120) || receipt?.Length > 120)
                return null;
            return new(Pin, id, GatewayEventKinds.RefundChanged, null, name, account,
                Refund: new(paymentId!, refundId!, receipt));
        }
        var kind = name switch
        {
            "payment.captured" or "order.paid" => GatewayEventKinds.PaymentCaptured,
            "payment.authorized" => GatewayEventKinds.PaymentAuthorized,
            "payment.failed" => GatewayEventKinds.PaymentFailed,
            _ => GatewayEventKinds.Other
        };
        var raw = Property(Property(Property(root, "payload"), "payment"), "entity");
        var payment = kind == GatewayEventKinds.Other ? null : ParsePayment(raw);
        var seconds = Property(root, "created_at");
        DateTimeOffset? occurred = seconds.ValueKind == JsonValueKind.Number && seconds.TryGetInt64(out var value) &&
            value is > 0 and <= 253_402_300_799 ? DateTimeOffset.FromUnixTimeSeconds(value) : null;
        return new(Pin, id, kind, payment, kind == GatewayEventKinds.Other ? null : name, account, occurred);
    }

    private GatewayEventPayment? ParsePayment(JsonElement raw)
    {
        if (RazorpayWire.Payment(raw) is not { } wire ||
            !wire.OrderId.StartsWith("order_", StringComparison.Ordinal) || !WebhookSignature.Identifier(wire.OrderId, 120) ||
            !WebhookSignature.Identifier(wire.Id, 120) || wire.Currency.Length != 3 ||
            !wire.Currency.All(char.IsAsciiLetterUpper))
            return null;
        return new(wire.OrderId, wire.Id, RazorpayWire.Observe(Pin, wire).State, wire.Amount, wire.Currency);
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var found) ? found : default;

    private static string? Text(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } found ? found.GetString() : null;
}
