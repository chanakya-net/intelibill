using System.Globalization;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;

namespace Intelibill.Licensing.Infrastructure.Payments.Razorpay;

/// <summary>The provider's order entity, reduced to the fields licensing relies on.</summary>
internal sealed record WireOrder(string Id, long Amount, string Currency, string? Receipt, string? Status,
    string? AccountNote, string? OrderNote, string? AttemptNote, bool PartialPayment, long? AmountPaid = null, long? AmountDue = null);

/// <summary>The provider's payment entity, reduced to the fields licensing relies on.</summary>
internal sealed record WirePayment(string Id, string OrderId, long Amount, string Currency, string Status, bool Captured,
    DateTimeOffset CreatedAt, long? AmountCaptured = null, long? AmountRefunded = null, string? RefundStatus = null,
    string? Method = null);

/// <summary>Strict parsers for documented Razorpay entities; anything unexpected is a rejection, never a guess.</summary>
internal static class RazorpayWire
{
    internal static WireOrder? Order(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !Text(item, "entity", out var entity) || entity != "order" ||
            !Text(item, "id", out var id) || !id.StartsWith("order_", StringComparison.Ordinal) || id.Length > 120 ||
            !Integer(item, "amount", out var amount) || !Text(item, "currency", out var currency))
            return null;
        Text(item, "receipt", out var receipt);
        Text(item, "status", out var status);
        string? account = null, order = null, attempt = null;
        if (item.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Object)
        {
            Text(notes, "account_id", out account);
            Text(notes, "order_id", out order);
            Text(notes, "attempt_id", out attempt);
        }
        var partial = false;
        if (item.TryGetProperty("partial_payment", out var flag))
        {
            if (flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return null;
            partial = flag.ValueKind == JsonValueKind.True;
        }
        if (!OptionalInteger(item, "amount_paid", out var paid) || !OptionalInteger(item, "amount_due", out var due))
            return null;
        return new(id, amount, currency, receipt, status, account, order, attempt, partial, paid, due);
    }

    internal static WirePayment? Payment(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !Text(item, "entity", out var entity) || entity != "payment" ||
            !Text(item, "id", out var id) || !id.StartsWith("pay_", StringComparison.Ordinal) || id.Length > 120 ||
            !Text(item, "order_id", out var order) || !Integer(item, "amount", out var amount) ||
            !Text(item, "currency", out var currency) || !Text(item, "status", out var status) ||
            !Integer(item, "created_at", out var created) || created <= 0 || created > 253_402_300_799 ||
            !item.TryGetProperty("captured", out var captured) ||
            captured.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return null;
        if (!OptionalInteger(item, "amount_captured", out var capturedAmount) ||
            !OptionalInteger(item, "amount_refunded", out var refunded) || !OptionalText(item, "refund_status", out var refund) ||
            !OptionalText(item, "method", out var method))
            return null;
        return new(id, order, amount, currency, status, captured.ValueKind == JsonValueKind.True,
            DateTimeOffset.FromUnixTimeSeconds(created), capturedAmount, refunded, refund, method);
    }

    /// <summary>Items of a documented <c>collection</c> response, or null when the shape is wrong.</summary>
    internal static IReadOnlyList<JsonElement>? Items(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !Text(root, "entity", out var entity) || entity != "collection" ||
            !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 100)
            return null;
        return [.. items.EnumerateArray()];
    }

    /// <summary>
    /// Razorpay's <c>created_at</c> is the payment's creation time. It is reported as such and never as the capture
    /// instant, because a payment may be authorized first and captured later.
    /// </summary>
    internal static PaymentObservation Observe(PaymentProviderPin pin, WirePayment payment) => new(pin, payment.OrderId, payment.Id,
        payment.Captured && payment.Status == "captured" ? ObservedPaymentState.Captured
            : payment.Status == "authorized" ? ObservedPaymentState.Authorized
            : payment.Status == "failed" ? ObservedPaymentState.Failed : ObservedPaymentState.Pending,
        payment.Amount, payment.Currency, null, CreatedAt: payment.CreatedAt);

    /// <summary>
    /// One exact, unrefunded full capture, stated explicitly and identically by the paid order, the order's payment
    /// collection entry and the payment's own read. Only <c>amount_captured</c> may be absent, as documented.
    /// </summary>
    internal static bool FullCapture(WirePayment entity, WirePayment entry, WireOrder order) =>
        entry == entity && PaidInFull(order) && entity.Captured && entity.Status == "captured" &&
        entity.Amount == order.Amount && entity.Currency == order.Currency &&
        (entity.AmountCaptured ?? entity.Amount) == entity.Amount && entity.AmountRefunded == 0 &&
        entity.RefundStatus is null or "";

    /// <summary>The order states it is paid with explicit totals: the full amount paid and nothing due.</summary>
    internal static bool PaidInFull(WireOrder order) =>
        order.Status == "paid" && order.AmountPaid == order.Amount && order.AmountDue == 0;

    /// <summary>
    /// The order is provably unpaid and may be resumed: still open, explicitly nothing paid and the full amount due,
    /// and every payment made against it failed.
    /// </summary>
    internal static bool ConfirmedUnpaid(WireOrder order, IReadOnlyList<WirePayment> payments) =>
        order.Status is "created" or "attempted" && order.AmountPaid == 0 && order.AmountDue == order.Amount &&
        payments.All(payment => payment is { Status: "failed", Captured: false });

    /// <summary>Anything that says money may have moved: a paid status, a paid or reduced due total.</summary>
    internal static bool ClaimsFunds(WireOrder order) =>
        order.Status == "paid" || order.AmountPaid is > 0 || order.AmountDue is { } due && due != order.Amount;

    private static bool Text(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String ||
            property.GetString() is not { Length: > 0 and <= 200 } text)
            return false;
        value = text;
        return true;
    }

    private static bool Integer(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out value) && value >= 0;
    }


    private static bool OptionalInteger(JsonElement element, string name, out long? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return true;
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var number) || number < 0)
            return false;
        value = number;
        return true;
    }

    private static bool OptionalText(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return true;
        if (property.ValueKind != JsonValueKind.String || property.GetString() is not { Length: <= 200 } text)
            return false;
        value = text;
        return true;
    }

    internal static string Receipt(Guid attempt) => attempt.ToString("N", CultureInfo.InvariantCulture);
}
