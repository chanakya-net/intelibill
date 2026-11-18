using System.Text.Json;
using Intelibill.Licensing.Application.Payments;

namespace Intelibill.Licensing.Infrastructure.Payments.Development;

internal sealed class DevelopmentWebhookOptions
{
    public string? WebhookSecret { get; init; }
}

internal sealed partial class DevelopmentPaymentGateway
{
    public GatewayResult<VerifiedGatewayEvent> ValidateWebhook(ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> headers)
    {
        if (!WebhookSignature.Secret(webhook.WebhookSecret))
            return GatewayResult.Unsupported<VerifiedGatewayEvent>();
        if (!headers.TryGetValue("X-Intelibill-Event-Id", out var id) || !WebhookSignature.Identifier(id, 120) ||
            !headers.TryGetValue("X-Intelibill-Webhook-Signature", out var signature) ||
            !WebhookSignature.Matches(body, signature, [webhook.WebhookSecret!]))
            return GatewayResult.Rejected<VerifiedGatewayEvent>();

        try
        {
            var reader = new Utf8JsonReader(body, new JsonReaderOptions { MaxDepth = 32 });
            using var data = JsonDocument.ParseValue(ref reader);
            return !reader.Read() && ParseEvent(data.RootElement, id) is { } parsed
                ? GatewayResult.Success(parsed) : GatewayResult.Rejected<VerifiedGatewayEvent>();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return GatewayResult.Rejected<VerifiedGatewayEvent>();
        }
    }

    private VerifiedGatewayEvent? ParseEvent(JsonElement root, string id)
    {
        var kind = Text(root, "kind");
        var state = kind switch
        {
            GatewayEventKinds.PaymentCaptured => ObservedPaymentState.Captured,
            GatewayEventKinds.PaymentAuthorized => ObservedPaymentState.Authorized,
            GatewayEventKinds.PaymentFailed => ObservedPaymentState.Failed,
            _ => (ObservedPaymentState?)null
        };
        var order = Text(root, "orderReference");
        if (state is null || order is null || !order.StartsWith("dev_order_", StringComparison.Ordinal) ||
            !WebhookSignature.Identifier(order, 120))
            return null;

        var payment = new GatewayEventPayment(order, State: state);
        return new(Pin, id, kind!, payment, kind, Pin.ProviderAccountId);
    }

    private static string? Text(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String ? found.GetString() : null;
}
